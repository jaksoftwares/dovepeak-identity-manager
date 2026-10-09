using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.8 — OAuth scopes reach issued tokens and are enforced by a resource server.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class ScopeApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task GrantedScope_AppearsInToken_AndIsEnforcedByProtectedApi()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Development}/scopes", new { name = "orders:read", description = "Read orders" })).Expect(HttpStatusCode.Created);
        var app = await CreateMachineAsync(scenario, ["orders:read"]);
        using var example = ExampleApi.For(Issuer(scenario));

        // Requested and allowed: the token carries the scope and the API accepts it.
        var granted = await TokenAsync(scenario, app, "orders:read");
        Assert.Contains("orders:read", Scopes(granted));
        Assert.Equal(HttpStatusCode.OK, await ExampleApi.CallAsync(example, "/orders", granted));

        // Not requested: optional scopes are never added implicitly.
        var plain = await TokenAsync(scenario, app, scope: null);
        Assert.DoesNotContain("orders:read", Scopes(plain));
        Assert.Equal(HttpStatusCode.Forbidden, await ExampleApi.CallAsync(example, "/orders", plain));
    }

    [Fact]
    public async Task ApplicationWithoutTheScope_CannotObtainIt()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Development}/scopes", new { name = "orders:read" })).Expect(HttpStatusCode.Created);
        var app = await CreateMachineAsync(scenario, []);

        Assert.Equal("invalid_scope", await TokenErrorAsync(scenario, app, "orders:read"));
    }

    [Fact]
    public async Task UpdatingApplicationScopes_ChangesWhatTokensCanCarry()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Development}/scopes", new { name = "orders:read" })).Expect(HttpStatusCode.Created);
        (await scenario.Owner.PostAsync($"{scenario.Development}/scopes", new { name = "orders:write" })).Expect(HttpStatusCode.Created);
        var app = await CreateMachineAsync(scenario, ["orders:read"]);

        var updated = (await scenario.Owner.PatchAsync(AppPath(scenario, app), new { scopes = new[] { "orders:write" } })).Expect(HttpStatusCode.OK).Json;
        Assert.Equal(["orders:write"], updated["scopes"]!.AsArray().Select(s => s!.GetValue<string>()));

        Assert.Contains("orders:write", Scopes(await TokenAsync(scenario, app, "orders:write")));
        Assert.Equal("invalid_scope", await TokenErrorAsync(scenario, app, "orders:read"));
    }

    [Fact]
    public async Task ScopeLifecycle_ValidatesNames_AndProtectsScopesInUse()
    {
        var scenario = await Scenario.CreateAsync(api);
        var scopes = $"{scenario.Development}/scopes";

        (await scenario.Owner.PostAsync(scopes, new { name = "orders:read" })).Expect(HttpStatusCode.Created);
        Assert.Equal("scope_exists", (await scenario.Owner.PostAsync(scopes, new { name = "orders:read" })).Expect(HttpStatusCode.Conflict).Code);
        (await scenario.Owner.PostAsync(scopes, new { name = "openid" })).Expect(HttpStatusCode.BadRequest);
        (await scenario.Owner.PostAsync(scopes, new { name = "offline_access" })).Expect(HttpStatusCode.BadRequest);
        (await scenario.Owner.PostAsync(scopes, new { name = "Orders Read" })).Expect(HttpStatusCode.BadRequest);

        // Applications may only request scopes the environment defines.
        (await scenario.Owner.PostAsync($"{scenario.Development}/applications", new { name = "svc-unknown", kind = "machine", scopes = new[] { "billing:read" } }))
            .Expect(HttpStatusCode.BadRequest);

        var app = await CreateMachineAsync(scenario, ["orders:read"]);
        Assert.Equal("scope_in_use", (await scenario.Owner.DeleteAsync($"{scopes}/orders:read")).Expect(HttpStatusCode.Conflict).Code);

        (await scenario.Owner.PatchAsync(AppPath(scenario, app), new { scopes = Array.Empty<string>() })).Expect(HttpStatusCode.OK);
        (await scenario.Owner.DeleteAsync($"{scopes}/orders:read")).Expect(HttpStatusCode.NoContent);
        Assert.Empty((await scenario.Owner.GetAsync(scopes)).Expect(HttpStatusCode.OK).Json.AsArray());
        Assert.Null(await FindEngineScopeAsync(scenario, "orders:read"));
    }

    [Fact]
    public async Task ScopeDrift_IsReverted()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Development}/scopes", new { name = "orders:read" })).Expect(HttpStatusCode.Created);
        var app = await CreateMachineAsync(scenario, ["orders:read"]);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var engineClientId = (await KeycloakAdmin.Client.FindClientIdAsync(realm, app["application"]!["clientId"]!.GetValue<string>(), CancellationToken.None))!;

        // Delete the scope in Keycloak (which also unassigns it) and promote nothing; reconciliation must restore both.
        await KeycloakAdmin.Client.DeleteClientScopeAsync(realm, (await FindEngineScopeAsync(scenario, "orders:read"))!, CancellationToken.None);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "scope_recreated" && c.Subject == "orders:read");
        Assert.Contains(corrections, c => c.Kind == "client_scopes_restored");
        var assigned = await KeycloakAdmin.Client.GetAssignedClientScopesAsync(realm, engineClientId, ClientScopeAssignment.Optional, CancellationToken.None);
        Assert.Contains("orders:read", assigned.Keys);
        Assert.Contains("orders:read", Scopes(await TokenAsync(scenario, app, "orders:read")));

        // A managed scope promoted to a default scope (always in tokens) is demoted again.
        var scopeId = (await FindEngineScopeAsync(scenario, "orders:read"))!;
        await KeycloakAdmin.Client.SetClientScopeAssignmentAsync(realm, engineClientId, scopeId, ClientScopeAssignment.Optional, false, CancellationToken.None);
        await KeycloakAdmin.Client.SetClientScopeAssignmentAsync(realm, engineClientId, scopeId, ClientScopeAssignment.Default, true, CancellationToken.None);

        Assert.Contains(await api.ReconcileAsync(scenario.DevelopmentId), c => c.Kind == "client_scopes_restored");
        Assert.DoesNotContain("orders:read", Scopes(await TokenAsync(scenario, app, scope: null)));
        Assert.Empty(await api.ReconcileAsync(scenario.DevelopmentId));
    }

    private static async Task<JsonNode> CreateMachineAsync(Scenario scenario, string[] scopes) =>
        (await scenario.Owner.PostAsync($"{scenario.Development}/applications", new
        {
            name = $"svc-{Guid.NewGuid():N}"[..20],
            kind = "machine",
            audiences = new[] { ExampleApi.Audience },
            scopes,
        })).Expect(HttpStatusCode.Created).Json;

    private static async Task<string> TokenAsync(Scenario scenario, JsonNode app, string? scope)
    {
        using var client = new OidcClient(Issuer(scenario), app["application"]!["clientId"]!.GetValue<string>(),
            app["clientSecret"]!.GetValue<string>(), TestRealm.RedirectUri);
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };
        if (scope is not null)
        {
            form["scope"] = scope;
        }

        using var response = await client.PostTokenAsync(form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
    }

    /// <summary>Requesting a scope the application was not granted is refused outright (RFC 6749 §5.2 invalid_scope).</summary>
    private static async Task<string> TokenErrorAsync(Scenario scenario, JsonNode app, string scope)
    {
        using var client = new OidcClient(Issuer(scenario), app["application"]!["clientId"]!.GetValue<string>(),
            app["clientSecret"]!.GetValue<string>(), TestRealm.RedirectUri);
        using var response = await client.PostTokenAsync(new() { ["grant_type"] = "client_credentials", ["scope"] = scope });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>();
    }

    private static string[] Scopes(string accessToken) =>
        new JwtSecurityToken(accessToken).Claims.FirstOrDefault(c => c.Type == "scope")?.Value.Split(' ') ?? [];

    private static async Task<string?> FindEngineScopeAsync(Scenario scenario, string name) =>
        (await KeycloakAdmin.Client.ListClientScopesAsync(RealmName.ForEnvironment(scenario.DevelopmentId), CancellationToken.None))
            .FirstOrDefault(s => s["name"]!.GetValue<string>() == name)?["id"]!.GetValue<string>();

    private static Uri Issuer(Scenario scenario) =>
        new(StackSettings.Current.KeycloakUrl, $"realms/{RealmName.ForEnvironment(scenario.DevelopmentId)}");

    private static string AppPath(Scenario scenario, JsonNode app) =>
        $"{scenario.Development}/applications/{app["application"]!["id"]!.GetValue<string>()}";
}
