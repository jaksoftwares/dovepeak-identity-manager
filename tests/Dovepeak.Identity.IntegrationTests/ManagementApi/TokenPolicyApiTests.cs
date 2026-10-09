using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.5 — per-application token and session policies are honoured by tokens Keycloak actually issues.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class TokenPolicyApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task ApplicationWithoutPolicy_InheritsTheEnvironmentBaseline()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var appPath = AppPath(scenario, created);

        var policy = created["application"]!["tokenPolicy"]!;
        Assert.Null(policy["accessTokenLifetimeSeconds"]);
        Assert.Null(policy["sessionIdleTimeoutSeconds"]);

        var config = (await scenario.Owner.GetAsync($"{appPath}/config")).Expect(HttpStatusCode.OK).Json;
        Assert.Equal(600, config["accessTokenLifetimeSeconds"]!.GetValue<int>());
        Assert.Equal(1800, config["sessionIdleTimeoutSeconds"]!.GetValue<int>());
        Assert.Equal(43200, config["sessionMaxLifetimeSeconds"]!.GetValue<int>());

        var tokens = await SignInAsync(scenario, created);
        Assert.Equal(600, tokens.ExpiresIn);
        Assert.Equal(1800, tokens.RefreshExpiresIn);
    }

    [Fact]
    public async Task ConfiguredPolicy_IsHonouredByIssuedTokens()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await CreateSpaAsync(scenario, new { accessTokenLifetimeSeconds = 300, sessionIdleTimeoutSeconds = 600, sessionMaxLifetimeSeconds = 3600 });

        var policy = created["application"]!["tokenPolicy"]!;
        Assert.Equal(300, policy["accessTokenLifetimeSeconds"]!.GetValue<int>());
        Assert.Equal(600, policy["sessionIdleTimeoutSeconds"]!.GetValue<int>());
        Assert.Equal(3600, policy["sessionMaxLifetimeSeconds"]!.GetValue<int>());

        var tokens = await SignInAsync(scenario, created);
        Assert.Equal(300, tokens.ExpiresIn);
        Assert.Equal(600, tokens.RefreshExpiresIn);

        // A refreshed token keeps the application's lifetime.
        using var client = Client(scenario, created);
        var refreshed = await client.RefreshAsync(tokens.RefreshToken!);
        Assert.Equal(300, refreshed.ExpiresIn);
    }

    [Fact]
    public async Task UpdatingPolicy_AppliesToNewTokens_AndAnEmptyPolicyRestoresTheBaseline()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await CreateSpaAsync(scenario, new { accessTokenLifetimeSeconds = 300, sessionIdleTimeoutSeconds = 600 });
        var appPath = AppPath(scenario, created);

        // A supplied policy replaces the previous one: the idle timeout is no longer set.
        var updated = (await scenario.Owner.PatchAsync(appPath, new { tokenPolicy = new { accessTokenLifetimeSeconds = 900 } })).Expect(HttpStatusCode.OK).Json;
        Assert.Equal(900, updated["tokenPolicy"]!["accessTokenLifetimeSeconds"]!.GetValue<int>());
        Assert.Null(updated["tokenPolicy"]!["sessionIdleTimeoutSeconds"]);

        var tokens = await SignInAsync(scenario, created);
        Assert.Equal(900, tokens.ExpiresIn);
        Assert.Equal(1800, tokens.RefreshExpiresIn);

        // Patching other fields leaves the policy alone.
        (await scenario.Owner.PatchAsync(appPath, new { name = "renamed-app" })).Expect(HttpStatusCode.OK);
        Assert.Equal(900, (await SignInAsync(scenario, created)).ExpiresIn);

        (await scenario.Owner.PatchAsync(appPath, new { tokenPolicy = new { } })).Expect(HttpStatusCode.OK);
        Assert.Equal(600, (await SignInAsync(scenario, created)).ExpiresIn);

        var audit = (await scenario.Owner.GetAsync($"{scenario.Org}/audit-events?source=management")).Expect(HttpStatusCode.OK).Json["events"]!.AsArray();
        Assert.Contains(audit, e => e!["type"]!.GetValue<string>() == "application.updated" && e.ToJsonString().Contains("tokenPolicy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MachineApplication_HonoursAccessTokenLifetime_AndRejectsSessionTimeouts()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = (await scenario.Owner.PostAsync($"{scenario.Development}/applications", new
        {
            name = "svc-short-tokens",
            kind = "machine",
            audiences = new[] { "orders-api" },
            tokenPolicy = new { accessTokenLifetimeSeconds = 300 },
        })).Expect(HttpStatusCode.Created).Json;

        using var client = new OidcClient(Issuer(scenario), created["application"]!["clientId"]!.GetValue<string>(),
            created["clientSecret"]!.GetValue<string>(), TestRealm.RedirectUri);
        using var response = await client.PostTokenAsync(new() { ["grant_type"] = "client_credentials" });
        response.EnsureSuccessStatusCode();
        Assert.Equal(300, (await response.Content.ReadFromJsonAsync<JsonObject>())!["expires_in"]!.GetValue<int>());

        var config = (await scenario.Owner.GetAsync($"{AppPath(scenario, created)}/config")).Expect(HttpStatusCode.OK).Json;
        Assert.Null(config["sessionIdleTimeoutSeconds"]);

        var rejected = (await scenario.Owner.PatchAsync(AppPath(scenario, created), new { tokenPolicy = new { sessionIdleTimeoutSeconds = 600 } }))
            .Expect(HttpStatusCode.BadRequest);
        Assert.Contains("no user sessions", rejected.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(120, null, null)]
    [InlineData(7200, null, null)]
    [InlineData(null, 86400, null)]
    [InlineData(null, 1200, 600)]
    public async Task InvalidPolicies_AreRejected_WithoutCreatingAClient(int? accessToken, int? idle, int? max)
    {
        var scenario = await Scenario.CreateAsync(api);
        var response = await scenario.Owner.PostAsync($"{scenario.Development}/applications", new
        {
            name = "invalid-policy",
            kind = "spa",
            redirectUris = new[] { "http://localhost:3999/callback" },
            tokenPolicy = new { accessTokenLifetimeSeconds = accessToken, sessionIdleTimeoutSeconds = idle, sessionMaxLifetimeSeconds = max },
        });

        response.Expect(HttpStatusCode.BadRequest);
        Assert.Contains("tokenPolicy", response.Body, StringComparison.Ordinal);
        var applications = (await scenario.Owner.GetAsync($"{scenario.Development}/applications")).Expect(HttpStatusCode.OK).Json.AsArray();
        Assert.Empty(applications);
    }

    private static async Task<JsonNode> CreateSpaAsync(Scenario scenario, object tokenPolicy) =>
        (await scenario.Owner.PostAsync($"{scenario.Development}/applications", new
        {
            name = $"app-{Guid.NewGuid():N}"[..20],
            kind = "spa",
            redirectUris = new[] { "http://localhost:3999/callback" },
            tokenPolicy,
        })).Expect(HttpStatusCode.Created).Json;

    private static async Task<TokenResponse> SignInAsync(Scenario scenario, JsonNode created)
    {
        var user = TestUser.Generate();
        await KeycloakAdmin.Client.CreateUserAsync(RealmName.ForEnvironment(scenario.DevelopmentId), user.Email, user.Password, true, CancellationToken.None);
        using var client = Client(scenario, created);
        return await SignIn.AsAsync(user, client);
    }

    private static OidcClient Client(Scenario scenario, JsonNode created) =>
        new(Issuer(scenario), created["application"]!["clientId"]!.GetValue<string>(), null, TestRealm.RedirectUri);

    private static Uri Issuer(Scenario scenario) =>
        new(StackSettings.Current.KeycloakUrl, $"realms/{RealmName.ForEnvironment(scenario.DevelopmentId)}");

    private static string AppPath(Scenario scenario, JsonNode created) =>
        $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";
}
