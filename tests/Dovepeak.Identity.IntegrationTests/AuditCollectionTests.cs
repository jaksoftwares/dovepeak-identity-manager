using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Audit;
using Dovepeak.Identity.Workers.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M2.5 — authentication and admin events reach the Dovepeak audit store, safely and once.</summary>
[Trait("Category", "Integration")]
public sealed class AuditCollectionTests(AuditCollectionTests.Fixture fixture) : IClassFixture<AuditCollectionTests.Fixture>
{
    [Fact]
    public async Task SignIn_IsRecordedInAuditStore_WithoutSecrets()
    {
        var (user, _) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);

        await fixture.CollectAsync();

        await using var db = fixture.CreateDbContext();
        var events = await db.AuditEvents
            .Where(e => e.Realm == fixture.Realm.Name.Value && e.UserId == user.Id)
            .ToListAsync();

        var login = Assert.Single(events, e => e.Type == "LOGIN");
        Assert.Equal(AuditSources.Authentication, login.Source);
        Assert.Equal("web-app", login.ClientId);
        Assert.NotNull(login.SessionId);
        Assert.Contains(events, e => e.Type == "CODE_TO_TOKEN");

        // Allow-listed details only: no code IDs, token IDs or authorization codes.
        foreach (var e in events)
        {
            var details = JsonNode.Parse(e.Details)!.AsObject();
            Assert.DoesNotContain(details, d => d.Key is "code_id" or "token_id" or "refresh_token_id" or "code");
        }
    }

    [Fact]
    public async Task FailedSignIn_IsRecordedWithError()
    {
        var user = await fixture.Realm.CreateUserAsync();
        var request = fixture.Realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        var page = (await browser.GetAsync(request.Url)).RequirePage();
        await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = user.Email,
            ["password"] = "wrong-password",
        });

        await fixture.CollectAsync();

        await using var db = fixture.CreateDbContext();
        var failure = await db.AuditEvents.SingleAsync(e =>
            e.Realm == fixture.Realm.Name.Value && e.UserId == user.Id && e.Type == "LOGIN_ERROR");
        Assert.Equal("invalid_user_credentials", failure.Error);
        Assert.DoesNotContain("wrong-password", failure.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminChanges_AreRecorded()
    {
        var user = await fixture.Realm.CreateUserAsync();

        await fixture.CollectAsync();

        await using var db = fixture.CreateDbContext();
        var created = await db.AuditEvents.Where(e =>
            e.Realm == fixture.Realm.Name.Value && e.Source == AuditSources.Admin && e.Type == "CREATE:USER").ToListAsync();
        Assert.Contains(created, e => e.Details.Contains(user.Id!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collection_IsIdempotent()
    {
        await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);
        await fixture.CollectAsync();

        await using var db = fixture.CreateDbContext();
        var before = await db.AuditEvents.CountAsync(e => e.Realm == fixture.Realm.Name.Value);

        Assert.Equal(0, await fixture.CollectAsync());
        Assert.Equal(before, await db.AuditEvents.CountAsync(e => e.Realm == fixture.Realm.Name.Value));
    }

    public sealed class Fixture : IAsyncLifetime, IDisposable
    {
        private readonly SemaphoreSlim _collectLock = new(1, 1);
        private ServiceProvider _services = null!;

        public TestRealm Realm { get; } = new();

        public async Task InitializeAsync()
        {
            var settings = StackSettings.Current;
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = settings.PostgresConnection,
                    ["Keycloak:BaseUrl"] = settings.KeycloakAdminUrl.ToString(),
                    ["Keycloak:ClientId"] = settings.ManagementClientId,
                    ["Keycloak:ClientSecret"] = settings.ManagementClientSecret,
                })
                .Build();

            var services = new ServiceCollection();
            services.AddKeycloakAdmin(configuration);
            services.AddPlatformPersistence(configuration);
            services.AddScoped<AuditEventCollector>();
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
            _services = services.BuildServiceProvider();

            await using (var db = CreateDbContext())
            {
                await db.Database.MigrateAsync();
            }

            await Realm.InitializeAsync();
        }

        public PlatformDbContext CreateDbContext() =>
            _services.CreateScope().ServiceProvider.GetRequiredService<PlatformDbContext>();

        /// <summary>Runs one collection pass for the test realm. Serialized, as the worker runs one pass at a time.</summary>
        public async Task<int> CollectAsync()
        {
            await _collectLock.WaitAsync();
            try
            {
                await using var scope = _services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AuditEventCollector>()
                    .CollectRealmAsync(Realm.Name, CancellationToken.None);
            }
            finally
            {
                _collectLock.Release();
            }
        }

        public async Task DisposeAsync()
        {
            await Realm.DisposeAsync();
            await _services.DisposeAsync();
        }

        public void Dispose() => _collectLock.Dispose();
    }
}
