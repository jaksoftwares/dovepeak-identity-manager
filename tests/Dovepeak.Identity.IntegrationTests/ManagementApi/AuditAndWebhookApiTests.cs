using System.Collections.Concurrent;
using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Platform.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.9 — audit trail, webhooks and idempotency.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class AuditAndWebhookApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task ManagementActions_AreAudited_WithActor()
    {
        var scenario = await Scenario.CreateAsync(api);
        await scenario.CreateApplicationAsync("web");

        var events = (await scenario.Owner.GetAsync($"{scenario.Org}/audit-events?source=management")).Expect(HttpStatusCode.OK).Json["events"]!.AsArray();
        var types = events.Select(e => e!["type"]!.GetValue<string>()).ToList();

        Assert.Contains("organization.created", types);
        Assert.Contains("project.created", types);
        Assert.Contains("environment.ready", types);
        Assert.Contains("application.created", types);
        var created = events.First(e => e!["type"]!.GetValue<string>() == "application.created")!;
        Assert.Equal("user", created["actorType"]!.GetValue<string>());
        Assert.Equal(scenario.Owner.UserId, created["actorId"]!.GetValue<string>());
        Assert.DoesNotContain("secret", created.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuditLog_IncludesOnlyThisOrganizationsSignInEvents()
    {
        var scenario = await Scenario.CreateAsync(api);
        var other = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var user = TestUser.Generate();
        await KeycloakAdmin.Client.CreateUserAsync(realm, user.Email, user.Password, true, CancellationToken.None);
        using var client = new OidcClient(new Uri(StackSettings.Current.KeycloakUrl, $"realms/{realm}"),
            created["application"]!["clientId"]!.GetValue<string>(), null, TestRealm.RedirectUri);
        await SignIn.AsAsync(user, client);

        await CollectAuthenticationEventsAsync(realm);

        var mine = (await scenario.Owner.GetAsync($"{scenario.Org}/audit-events?source=authentication")).Expect(HttpStatusCode.OK).Json["events"]!.AsArray();
        var theirs = (await other.Owner.GetAsync($"{other.Org}/audit-events?source=authentication")).Expect(HttpStatusCode.OK).Json["events"]!.AsArray();

        Assert.Contains(mine, e => e!["type"]!.GetValue<string>() == "LOGIN");
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task ManagementAuditLog_IsAppendOnly()
    {
        var scenario = await Scenario.CreateAsync(api);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterOrganization(scenario.OrganizationId);

        var update = () => db.Database.ExecuteSqlRawAsync("UPDATE management_audit_events SET action = 'tampered'");
        var delete = () => db.Database.ExecuteSqlRawAsync("DELETE FROM management_audit_events");

        Assert.Contains("append-only", (await Assert.ThrowsAnyAsync<Exception>(update)).Message, StringComparison.Ordinal);
        Assert.Contains("append-only", (await Assert.ThrowsAnyAsync<Exception>(delete)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Webhook_DeliversSignedEvents_AndSecretIsShownOnce()
    {
        var scenario = await Scenario.CreateAsync(api);
        await using var receiver = await WebhookReceiver.StartAsync();

        var created = (await scenario.Owner.PostAsync($"{scenario.Org}/webhooks", new { url = receiver.Url, eventTypes = new[] { "application.created" } }))
            .Expect(HttpStatusCode.Created).Json;
        var secret = created["signingSecret"]!.GetValue<string>();
        var webhookId = created["webhook"]!["id"]!.GetValue<string>();
        Assert.DoesNotContain(secret, (await scenario.Owner.GetAsync($"{scenario.Org}/webhooks")).Body, StringComparison.Ordinal);

        await scenario.CreateApplicationAsync("spa", name: "webhook-app");
        await api.DeliverWebhooksAsync();

        var received = Assert.Single(receiver.Requests);
        Assert.Equal("application.created", received.EventType);
        Assert.True(WebhookSigner.Verify(secret, received.Signature, received.Body, DateTimeOffset.UtcNow));
        Assert.False(WebhookSigner.Verify("whsec_wrong", received.Signature, received.Body, DateTimeOffset.UtcNow));
        Assert.Contains("webhook-app", received.Body, StringComparison.Ordinal);

        var deliveries = (await scenario.Owner.GetAsync($"{scenario.Org}/webhooks/{webhookId}/deliveries")).Expect(HttpStatusCode.OK).Json.AsArray();
        Assert.Equal("delivered", Assert.Single(deliveries)!["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://user:pass@example.com/hook")]
    public async Task UnsafeWebhookUrls_AreRejected(string url)
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Org}/webhooks", new { url })).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IdempotencyKey_ReplaysTheOriginalResponse_WithoutRepeatingTheAction()
    {
        var scenario = await Scenario.CreateAsync(api);
        var key = Guid.NewGuid().ToString();
        var body = new { name = "idempotent-app", kind = "web", redirectUris = new[] { "http://localhost:3999/callback" } };

        var first = (await scenario.Owner.PostAsync($"{scenario.Development}/applications", body, key)).Expect(HttpStatusCode.Created);
        var replay = (await scenario.Owner.PostAsync($"{scenario.Development}/applications", body, key)).Expect(HttpStatusCode.Created);

        Assert.Equal(first.Json["application"]!["id"]!.GetValue<string>(), replay.Json["application"]!["id"]!.GetValue<string>());
        Assert.Equal("true", replay.Headers["Idempotent-Replayed"]);
        // The one-time secret is never stored for replay.
        Assert.NotNull(first.Json["clientSecret"]);
        Assert.Null(replay.Json["clientSecret"]);
        Assert.Equal("true", replay.Headers["Dovepeak-Secret-Omitted"]);

        var applications = (await scenario.Owner.GetAsync($"{scenario.Development}/applications")).Expect(HttpStatusCode.OK).Json.AsArray();
        Assert.Single(applications, a => a!["name"]!.GetValue<string>() == "idempotent-app");
    }

    [Fact]
    public async Task IdempotencyKey_ReusedWithDifferentBody_IsRejected()
    {
        var scenario = await Scenario.CreateAsync(api);
        var key = Guid.NewGuid().ToString();

        (await scenario.Owner.PostAsync($"{scenario.Org}/projects", new { slug = "first", name = "First" }, key)).Expect(HttpStatusCode.Created);
        var reused = (await scenario.Owner.PostAsync($"{scenario.Org}/projects", new { slug = "second", name = "Second" }, key)).Expect(HttpStatusCode.BadRequest);

        Assert.Equal("idempotency_key_reused", reused.Code);
        await api.ProcessOutboxAsync();
    }

    private async Task CollectAuthenticationEventsAsync(RealmName realm)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var collector = ActivatorUtilities.CreateInstance<Workers.Audit.AuditEventCollector>(scope.ServiceProvider);
        await collector.CollectRealmAsync(realm, CancellationToken.None);
    }

    /// <summary>A real HTTP endpoint that records webhook deliveries, as a customer's server would.</summary>
    private sealed class WebhookReceiver : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private WebhookReceiver(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public string Url { get; }

        public ConcurrentBag<ReceivedWebhook> Requests { get; } = [];

        public static async Task<WebhookReceiver> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            WebhookReceiver? receiver = null;
            app.MapPost("/hook", async (HttpRequest request) =>
            {
                using var reader = new StreamReader(request.Body);
                receiver!.Requests.Add(new ReceivedWebhook(
                    request.Headers[WebhookSigner.EventTypeHeader].ToString(),
                    request.Headers[WebhookSigner.SignatureHeader].ToString(),
                    await reader.ReadToEndAsync()));
                return Results.Ok();
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            receiver = new WebhookReceiver(app, $"{address}/hook");
            return receiver;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private sealed record ReceivedWebhook(string EventType, string Signature, string Body);
}
