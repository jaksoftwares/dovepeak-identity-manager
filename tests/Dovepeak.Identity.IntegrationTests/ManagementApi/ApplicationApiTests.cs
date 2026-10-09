using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.5 — applications are real OAuth clients, configured only through the Management API.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class ApplicationApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task SpaApplication_HasNoSecret_AndUsersCanSignInThroughIt()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");

        Assert.Null(created["clientSecret"]?.GetValue<string>());
        var clientId = created["application"]!["clientId"]!.GetValue<string>();
        Assert.StartsWith("app_", clientId, StringComparison.Ordinal);

        // End to end: a user of this environment signs in through the newly registered application.
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var user = TestUser.Generate();
        await KeycloakAdmin.Client.CreateUserAsync(realm, user.Email, user.Password, emailVerified: true, CancellationToken.None);
        using var client = new OidcClient(new Uri(StackSettings.Current.KeycloakUrl, $"realms/{realm}"), clientId, null, TestRealm.RedirectUri);
        var tokens = await SignIn.AsAsync(user, client);

        Assert.Contains("dovepeak-demo-api", new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(tokens.AccessToken).Audiences);
    }

    [Fact]
    public async Task WebApplication_SecretIsShownOnce_AndNeverReturnedAgain()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("web");
        var applicationId = created["application"]!["id"]!.GetValue<string>();
        var appPath = $"{scenario.Development}/applications/{applicationId}";

        var secret = created["clientSecret"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(secret));

        var get = (await scenario.Owner.GetAsync(appPath)).Expect(HttpStatusCode.OK);
        var list = (await scenario.Owner.GetAsync($"{scenario.Development}/applications")).Expect(HttpStatusCode.OK);
        var config = (await scenario.Owner.GetAsync($"{appPath}/config")).Expect(HttpStatusCode.OK);

        foreach (var response in new[] { get, list, config })
        {
            Assert.DoesNotContain(secret, response.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"clientSecret\"", response.Body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("client_secret_post", config.Json["tokenEndpointAuthMethod"]!.GetValue<string>());
        Assert.EndsWith("/.well-known/openid-configuration", config.Json["discoveryUrl"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://app.example.com/*")]
    [InlineData("http://app.example.com/callback")]
    [InlineData("not-a-uri")]
    public async Task UnsafeRedirectUris_AreRejected(string redirectUri)
    {
        var scenario = await Scenario.CreateAsync(api);

        var response = await scenario.Owner.PostAsync($"{scenario.Development}/applications",
            new { name = "bad", kind = "spa", redirectUris = new[] { redirectUri } });

        response.Expect(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatingRedirectUris_IsAppliedToTheEngine()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var appPath = $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";

        (await scenario.Owner.PatchAsync(appPath, new { redirectUris = new[] { "https://app.example.com/auth/callback" } })).Expect(HttpStatusCode.OK);

        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var engineId = await KeycloakAdmin.Client.FindClientIdAsync(realm, created["application"]!["clientId"]!.GetValue<string>(), CancellationToken.None);
        var client = await KeycloakAdmin.Client.GetClientAsync(realm, engineId!, CancellationToken.None);
        Assert.Equal(["https://app.example.com/auth/callback"], client["redirectUris"]!.AsArray().Select(u => u!.GetValue<string>()));
    }

    [Fact]
    public async Task DeletingApplication_RemovesTheClient()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var clientId = created["application"]!["clientId"]!.GetValue<string>();

        (await scenario.Owner.DeleteAsync($"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}"))
            .Expect(HttpStatusCode.NoContent);

        Assert.Null(await KeycloakAdmin.Client.FindClientIdAsync(RealmName.ForEnvironment(scenario.DevelopmentId), clientId, CancellationToken.None));
    }

    [Fact]
    public async Task ApplicationNames_AreUniquePerEnvironment()
    {
        var scenario = await Scenario.CreateAsync(api);
        await scenario.CreateApplicationAsync("spa", name: "storefront");

        var duplicate = await scenario.Owner.PostAsync($"{scenario.Development}/applications",
            new { name = "storefront", kind = "spa", redirectUris = new[] { "http://localhost:3999/callback" } });

        Assert.Equal("name_taken", duplicate.Expect(HttpStatusCode.Conflict).Code);
    }
}
