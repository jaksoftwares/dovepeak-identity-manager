using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.6 — client secret rotation and developer API keys.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class CredentialApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task RotatingSecret_InvalidatesTheOldOneImmediately()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("machine", audiences: ["orders-api"]);
        var clientId = created["application"]!["clientId"]!.GetValue<string>();
        var oldSecret = created["clientSecret"]!.GetValue<string>();
        var appPath = $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";
        var issuer = new Uri(StackSettings.Current.KeycloakUrl, $"realms/{RealmName.ForEnvironment(scenario.DevelopmentId)}");

        Assert.True(await ClientCredentialsAsync(issuer, clientId, oldSecret));

        var rotated = (await scenario.Owner.PostAsync($"{appPath}/secret")).Expect(HttpStatusCode.OK);
        var newSecret = rotated.Json["clientSecret"]!.GetValue<string>();

        Assert.Equal("no-store", rotated.Headers["Cache-Control"]);
        Assert.NotEqual(oldSecret, newSecret);
        Assert.False(await ClientCredentialsAsync(issuer, clientId, oldSecret));
        Assert.True(await ClientCredentialsAsync(issuer, clientId, newSecret));
    }

    [Fact]
    public async Task PublicClients_HaveNoSecretToRotate()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");

        var response = await scenario.Owner.PostAsync($"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}/secret");

        Assert.Equal("public_client", response.Expect(HttpStatusCode.Conflict).Code);
    }

    [Fact]
    public async Task ApiKey_IsShownOnce_WorksWithinItsScopes_AndRevokesImmediately()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "ci", scopes = new[] { "projects:read" } }))
            .Expect(HttpStatusCode.Created).Json;
        var key = created["key"]!.GetValue<string>();
        var keyId = created["apiKey"]!["id"]!.GetValue<string>();

        Assert.StartsWith("dpk_live_", key, StringComparison.Ordinal);
        var listing = (await scenario.Owner.GetAsync($"{scenario.Org}/api-keys")).Expect(HttpStatusCode.OK);
        Assert.DoesNotContain(key, listing.Body, StringComparison.Ordinal);
        Assert.Contains(key[..15], listing.Body, StringComparison.Ordinal);

        using var automation = api.Client(key);
        (await automation.GetAsync($"{scenario.Org}/projects")).Expect(HttpStatusCode.OK);
        (await automation.PostAsync($"{scenario.Org}/projects", new { slug = "nope", name = "Nope" })).Expect(HttpStatusCode.Forbidden);

        (await scenario.Owner.DeleteAsync($"{scenario.Org}/api-keys/{keyId}")).Expect(HttpStatusCode.NoContent);
        (await automation.GetAsync($"{scenario.Org}/projects")).Expect(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKey_CannotReachAnotherOrganization()
    {
        var mine = await Scenario.CreateAsync(api);
        var theirs = await Scenario.CreateAsync(api);
        var created = (await mine.Owner.PostAsync($"{mine.Org}/api-keys", new { name = "ci", scopes = new[] { "projects:read" } }))
            .Expect(HttpStatusCode.Created).Json;

        using var automation = api.Client(created["key"]!.GetValue<string>());

        (await automation.GetAsync($"{theirs.Org}/projects")).Expect(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("members:manage")]
    [InlineData("api-keys:manage")]
    [InlineData("organization:manage")]
    [InlineData("everything")]
    public async Task ApiKeys_CannotHoldHumanOnlyOrUnknownScopes(string scope)
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "bad", scopes = new[] { scope } })).Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ApiKey_CannotExceedItsCreatorsPermissions()
    {
        // Only owners and admins can create keys, and they hold every key-eligible permission, so the check is that
        // an API key itself cannot mint keys (api-keys:manage is human-only).
        var scenario = await Scenario.CreateAsync(api);
        var created = (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "ci", scopes = new[] { "projects:write" } }))
            .Expect(HttpStatusCode.Created).Json;
        using var automation = api.Client(created["key"]!.GetValue<string>());

        (await automation.PostAsync($"{scenario.Org}/api-keys", new { name = "child", scopes = new[] { "projects:write" } })).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExpiredOrMalformedKeys_AreRejected()
    {
        using var malformed = api.Client("dpk_live_notarealkey");
        (await malformed.GetAsync("/v1/organizations")).Expect(HttpStatusCode.Unauthorized);

        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys",
            new { name = "past", scopes = new[] { "projects:read" }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })).Expect(HttpStatusCode.BadRequest);
    }

    private static async Task<bool> ClientCredentialsAsync(Uri issuer, string clientId, string secret)
    {
        using var client = new OidcClient(issuer, clientId, secret, TestRealm.RedirectUri);
        using var response = await client.PostTokenAsync(new() { ["grant_type"] = "client_credentials" });
        return response.IsSuccessStatusCode;
    }
}
