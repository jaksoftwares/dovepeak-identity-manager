using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>
/// Milestone M5.4: a .NET API enforces roles and scopes using only the Dovepeak.Identity SDK, and automation uses
/// the SDK's typed Management API client, against the real Management API and Keycloak.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class DotnetSdkTests(ManagementApiFixture api)
{
    private const string Audience = "orders-api";

    [Fact]
    public async Task ManagementClient_AutomatesAnEnvironment_WithAnApiKey()
    {
        var scenario = await Scenario.CreateAsync(api);
        var management = await ManagementClientAsync(scenario);
        var (org, project, env) = (scenario.OrganizationId, scenario.ProjectId, scenario.DevelopmentId);

        var projects = await management.ListProjectsAsync(org);
        Assert.Contains(projects, p => p.Id == project);

        await management.CreateScopeAsync(org, project, env, "orders:read", "Read orders");
        var duplicate = await Assert.ThrowsAsync<DovepeakApiException>(() => management.CreateScopeAsync(org, project, env, "orders:read"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("scope_exists", duplicate.Code);

        var created = await management.CreateApplicationAsync(org, project, env,
            new NewApplication("order-sync", "machine", Audiences: [Audience], Scopes: ["orders:read"], TokenPolicy: new Management.TokenPolicy(AccessTokenLifetimeSeconds: 900)));
        Assert.False(string.IsNullOrEmpty(created.ClientSecret));
        Assert.Equal(900, created.Application.TokenPolicy.AccessTokenLifetimeSeconds);

        var config = await management.GetApplicationConfigAsync(org, project, env, created.Application.Id);
        Assert.Equal(created.Application.ClientId, config.ClientId);
        Assert.Equal(["orders:read"], config.Scopes);

        var rotated = await management.RotateSecretAsync(org, project, env, created.Application.Id);
        Assert.NotEqual(created.ClientSecret, rotated.ClientSecret);
        Assert.NotNull(rotated.PreviousSecretExpiresAt);

        var invalid = await Assert.ThrowsAsync<DovepeakApiException>(() =>
            management.CreateApplicationAsync(org, project, env, new NewApplication("bad", "spa", RedirectUris: ["https://app.example.test/*"])));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var audit = await management.GetAuditEventsAsync(org, source: "management");
        Assert.Contains(audit.Events, e => e.Type == "application.secret_rotated");

        await management.DeleteApplicationAsync(org, project, env, created.Application.Id);
        var missing = await Assert.ThrowsAsync<DovepeakApiException>(() => management.GetApplicationAsync(org, project, env, created.Application.Id));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SdkProtectedApi_EnforcesScopesAndRoles()
    {
        var scenario = await Scenario.CreateAsync(api);
        var management = await ManagementClientAsync(scenario);
        var (org, project, env) = (scenario.OrganizationId, scenario.ProjectId, scenario.DevelopmentId);
        var issuer = Issuer(scenario);

        await management.CreateScopeAsync(org, project, env, "orders:read");
        var machine = await management.CreateApplicationAsync(org, project, env, new NewApplication("svc", "machine", Audiences: [Audience], Scopes: ["orders:read"]));
        var spa = await management.CreateApplicationAsync(org, project, env,
            new NewApplication("web", "spa", RedirectUris: [TestRealm.RedirectUri.ToString()], Audiences: [Audience]));
        await management.CreateRoleAsync(org, project, env, spa.Application.Id, "administrator");

        await using var protectedApi = await StartProtectedApiAsync(issuer, introspection: null);
        using var http = protectedApi.GetTestClient();

        var scoped = await ClientCredentialsAsync(issuer, machine, "orders:read");
        var unscoped = await ClientCredentialsAsync(issuer, machine, scope: null);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync(http, "/orders", token: null));
        Assert.Equal(HttpStatusCode.OK, await CallAsync(http, "/orders", scoped));
        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync(http, "/orders", unscoped));
        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync(http, "/admin", scoped));

        // A user with the application role, assigned through the SDK.
        var user = TestUser.Generate();
        var userId = await KeycloakAdmin.Client.CreateUserAsync(RealmName.ForEnvironment(env), user.Email, user.Password, true, CancellationToken.None);
        using var spaClient = new OidcClient(issuer, spa.Application.ClientId, null, TestRealm.RedirectUri);
        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync(http, "/admin", (await SignIn.AsAsync(user, spaClient)).AccessToken));

        await management.AssignRoleAsync(org, project, env, spa.Application.Id, "administrator", userId);
        Assert.Equal(HttpStatusCode.OK, await CallAsync(http, "/admin", (await SignIn.AsAsync(user, spaClient)).AccessToken));
    }

    [Fact]
    public async Task IntrospectionMode_RejectsRevokedSessionsImmediately()
    {
        var scenario = await Scenario.CreateAsync(api);
        var management = await ManagementClientAsync(scenario);
        var (org, project, env) = (scenario.OrganizationId, scenario.ProjectId, scenario.DevelopmentId);
        var issuer = Issuer(scenario);

        // The API authenticates to the introspection endpoint with its own confidential client.
        var apiClient = await management.CreateApplicationAsync(org, project, env, new NewApplication("orders-api", "machine"));
        var spa = await management.CreateApplicationAsync(org, project, env,
            new NewApplication("web", "spa", RedirectUris: [TestRealm.RedirectUri.ToString()], Audiences: [Audience]));

        await using var protectedApi = await StartProtectedApiAsync(issuer, (apiClient.Application.ClientId, apiClient.ClientSecret!));
        using var http = protectedApi.GetTestClient();

        var user = TestUser.Generate();
        var userId = await KeycloakAdmin.Client.CreateUserAsync(RealmName.ForEnvironment(env), user.Email, user.Password, true, CancellationToken.None);
        using var spaClient = new OidcClient(issuer, spa.Application.ClientId, null, TestRealm.RedirectUri);
        var token = (await SignIn.AsAsync(user, spaClient)).AccessToken;
        Assert.Equal(HttpStatusCode.OK, await CallAsync(http, "/me", token));

        await management.RevokeSessionsAsync(org, project, env, userId);

        // The token has not expired, but its session has ended.
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync(http, "/me", token));
    }

    private async Task<DovepeakManagementClient> ManagementClientAsync(Scenario scenario)
    {
        var scopes = new[] { "projects:read", "projects:write", "applications:read", "applications:write", "credentials:manage", "users:manage", "audit:read" };
        var key = (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "sdk", scopes })).Expect(HttpStatusCode.Created)
            .Json["key"]!.GetValue<string>();
        var http = api.CreateHttpClient();
        return new DovepeakManagementClient(http, Options.Create(new DovepeakManagementOptions { BaseUrl = http.BaseAddress, ApiKey = key }));
    }

    /// <summary>A minimal API configured only through the SDK, hosted in memory.</summary>
    private static async Task<WebApplication> StartProtectedApiAsync(Uri issuer, (string ClientId, string Secret)? introspection)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDovepeakAuthentication(options =>
        {
            options.Issuer = issuer.ToString();
            options.Audience = Audience;
            options.RequireHttpsMetadata = false;
            if (introspection is { } credentials)
            {
                options.Introspection.Enabled = true;
                options.Introspection.ClientId = credentials.ClientId;
                options.Introspection.ClientSecret = credentials.Secret;
                options.Introspection.CacheSeconds = 0;
            }
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (HttpContext context) => context.User.Identity!.Name).RequireAuthorization();
        app.MapGet("/orders", () => "orders").RequireScope("orders:read");
        app.MapGet("/admin", () => "admin").RequireDovepeakRole("administrator");
        await app.StartAsync();
        return app;
    }

    private static async Task<string> ClientCredentialsAsync(Uri issuer, CreatedApplication machine, string? scope)
    {
        using var client = new OidcClient(issuer, machine.Application.ClientId, machine.ClientSecret, TestRealm.RedirectUri);
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (scope is not null)
        {
            form["scope"] = scope;
        }

        using var response = await client.PostTokenAsync(form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
    }

    private static async Task<HttpStatusCode> CallAsync(HttpClient http, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    private static Uri Issuer(Scenario scenario) =>
        new(StackSettings.Current.KeycloakUrl, $"realms/{RealmName.ForEnvironment(scenario.DevelopmentId)}");
}
