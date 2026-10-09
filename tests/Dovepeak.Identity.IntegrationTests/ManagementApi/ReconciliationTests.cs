using System.Net;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.7 — changes made directly in the identity engine are detected and reverted.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class ReconciliationTests(ManagementApiFixture api)
{
    [Fact]
    public async Task ManualClientChange_IsReverted()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var engineId = (await KeycloakAdmin.Client.FindClientIdAsync(realm, created["application"]!["clientId"]!.GetValue<string>(), CancellationToken.None))!;

        // An attacker (or a well-meaning administrator) adds a wildcard redirect and enables the password grant.
        var tampered = await KeycloakAdmin.Client.GetClientAsync(realm, engineId, CancellationToken.None);
        tampered["redirectUris"] = new JsonArray("https://attacker.example/*");
        tampered["directAccessGrantsEnabled"] = true;
        await PutClientAsync(realm, engineId, tampered);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "client_config_restored");
        var restored = await KeycloakAdmin.Client.GetClientAsync(realm, engineId, CancellationToken.None);
        Assert.Equal(["http://localhost:3999/callback"], restored["redirectUris"]!.AsArray().Select(u => u!.GetValue<string>()));
        Assert.False(restored["directAccessGrantsEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ManualTokenLifetimeChange_IsReverted()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = (await scenario.Owner.PostAsync($"{scenario.Development}/applications", new
        {
            name = "short-lived",
            kind = "spa",
            redirectUris = new[] { "http://localhost:3999/callback" },
            tokenPolicy = new { accessTokenLifetimeSeconds = 300 },
        })).Expect(HttpStatusCode.Created).Json;
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var engineId = (await KeycloakAdmin.Client.FindClientIdAsync(realm, created["application"]!["clientId"]!.GetValue<string>(), CancellationToken.None))!;

        // Day-long access tokens and an unbounded client session, set directly in Keycloak.
        var tampered = await KeycloakAdmin.Client.GetClientAsync(realm, engineId, CancellationToken.None);
        tampered["attributes"]!["access.token.lifespan"] = "86400";
        tampered["attributes"]!["client.session.idle.timeout"] = "86400";
        await PutClientAsync(realm, engineId, tampered);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "client_config_restored");
        var restored = (await KeycloakAdmin.Client.GetClientAsync(realm, engineId, CancellationToken.None))["attributes"]!;
        Assert.Equal("300", restored["access.token.lifespan"]!.GetValue<string>());
        Assert.Equal(string.Empty, restored["client.session.idle.timeout"]?.GetValue<string>() ?? string.Empty);
        Assert.Empty(await api.ReconcileAsync(scenario.DevelopmentId));
    }

    [Fact]
    public async Task DeletedClient_IsRecreated_AndUnknownClientIsRemoved()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var clientId = created["application"]!["clientId"]!.GetValue<string>();
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);

        var engineId = (await KeycloakAdmin.Client.FindClientIdAsync(realm, clientId, CancellationToken.None))!;
        await KeycloakAdmin.Client.DeleteClientAsync(realm, engineId, CancellationToken.None);
        await KeycloakAdmin.Client.CreateClientAsync(realm, new ClientRegistration("rogue-client", ClientKind.Public)
        {
            RedirectUris = [new Uri("https://rogue.example/callback")],
        }, CancellationToken.None);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "client_recreated" && c.Subject == clientId);
        Assert.Contains(corrections, c => c.Kind == "unknown_client_removed" && c.Subject == "rogue-client");
        Assert.NotNull(await KeycloakAdmin.Client.FindClientIdAsync(realm, clientId, CancellationToken.None));
        Assert.Null(await KeycloakAdmin.Client.FindClientIdAsync(realm, "rogue-client", CancellationToken.None));
    }

    [Fact]
    public async Task WeakenedRealmSettings_AreRestored()
    {
        var scenario = await Scenario.CreateAsync(api);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        await KeycloakAdmin.Client.UpdateRealmAsync(realm, new JsonObject
        {
            ["bruteForceProtected"] = false,
            ["accessTokenLifespan"] = 86400,
        }, CancellationToken.None);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "realm_settings_restored");
        var representation = (await KeycloakAdmin.Client.GetRealmAsync(realm, CancellationToken.None))!;
        Assert.True(representation["bruteForceProtected"]!.GetValue<bool>());
        Assert.Equal(600, representation["accessTokenLifespan"]!.GetValue<int>());
    }

    [Fact]
    public async Task RemovedOrWeakenedClientPolicies_AreRestored()
    {
        var scenario = await Scenario.CreateAsync(api);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);

        // Disabling the client policies would silently drop PKCE enforcement and overlapping secret rotation.
        await KeycloakAdmin.Client.UpdateClientPoliciesAsync(realm, new JsonObject { ["policies"] = new JsonArray() }, CancellationToken.None);
        var profiles = await KeycloakAdmin.Client.GetClientProfilesAsync(realm, CancellationToken.None);
        profiles["profiles"]![0]!["executors"] = new JsonArray();
        await KeycloakAdmin.Client.UpdateClientProfilesAsync(realm, profiles, CancellationToken.None);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "client_policies_restored");
        var policies = (await KeycloakAdmin.Client.GetClientPoliciesAsync(realm, CancellationToken.None))["policies"]!.AsArray();
        Assert.Equal(["dovepeak-secure-defaults", "dovepeak-secret-rotation"], policies.Select(p => p!["name"]!.GetValue<string>()));
        var restored = (await KeycloakAdmin.Client.GetClientProfilesAsync(realm, CancellationToken.None))["profiles"]!.AsArray();
        Assert.Contains("pkce-enforcer", restored[0]!["executors"]!.AsArray().Select(e => e!["executor"]!.GetValue<string>()));
        Assert.Empty(await api.ReconcileAsync(scenario.DevelopmentId));
    }

    [Fact]
    public async Task Corrections_AreAudited_AndIdempotent()
    {
        var scenario = await Scenario.CreateAsync(api);
        await KeycloakAdmin.Client.UpdateRealmAsync(RealmName.ForEnvironment(scenario.DevelopmentId),
            new JsonObject { ["verifyEmail"] = false }, CancellationToken.None);

        Assert.NotEmpty(await api.ReconcileAsync(scenario.DevelopmentId));
        Assert.Empty(await api.ReconcileAsync(scenario.DevelopmentId));

        var audit = (await scenario.Owner.GetAsync($"{scenario.Org}/audit-events?source=management")).Expect(HttpStatusCode.OK).Json;
        Assert.Contains(audit["events"]!.AsArray(), e => e!["type"]!.GetValue<string>() == "drift.corrected");
    }

    private static async Task PutClientAsync(RealmName realm, string id, JsonObject representation)
    {
        // Simulates an out-of-band change made with full admin rights, bypassing Dovepeak's validation.
        var token = await KeycloakAdmin.TokenProvider.GetTokenAsync(CancellationToken.None);
        using var http = new HttpClient { BaseAddress = StackSettings.Current.KeycloakAdminUrl };
        using var request = new HttpRequestMessage(HttpMethod.Put, $"admin/realms/{realm}/clients/{id}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(representation),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
