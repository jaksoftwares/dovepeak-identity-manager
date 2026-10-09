using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.8 — application roles reach tokens and are enforced by a resource server; user sessions are manageable.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class RoleAndUserApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task AssignedRole_AppearsInToken_AndIsEnforcedByProtectedApi()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var clientId = created["application"]!["clientId"]!.GetValue<string>();
        var appPath = $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var issuer = new Uri(StackSettings.Current.KeycloakUrl, $"realms/{realm}");

        (await scenario.Owner.PostAsync($"{appPath}/roles", new { name = "administrator", description = "Full access" })).Expect(HttpStatusCode.Created);

        var user = TestUser.Generate();
        var userId = await KeycloakAdmin.Client.CreateUserAsync(realm, user.Email, user.Password, true, CancellationToken.None);
        using var client = new OidcClient(issuer, clientId, null, TestRealm.RedirectUri);
        using var api2 = ExampleApi.For(issuer);

        // Without the role: authenticated, but forbidden on the admin endpoint.
        var before = await SignIn.AsAsync(user, client);
        Assert.Equal(HttpStatusCode.OK, await ExampleApi.CallAsync(api2, "/me", before.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, await ExampleApi.CallAsync(api2, "/admin", before.AccessToken));

        (await scenario.Owner.PutAsync($"{appPath}/roles/administrator/users/{userId}")).Expect(HttpStatusCode.NoContent);

        var after = await SignIn.AsAsync(user, client);
        Assert.Contains("administrator", new JwtSecurityToken(after.AccessToken).Claims.Where(c => c.Type == "roles").Select(c => c.Value));
        Assert.Equal(HttpStatusCode.OK, await ExampleApi.CallAsync(api2, "/admin", after.AccessToken));
    }

    [Fact]
    public async Task AssigningUnknownRoleOrUser_ReturnsNotFound()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var appPath = $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";
        (await scenario.Owner.PostAsync($"{appPath}/roles", new { name = "editor" })).Expect(HttpStatusCode.Created);

        (await scenario.Owner.PutAsync($"{appPath}/roles/missing/users/{Guid.NewGuid()}")).Expect(HttpStatusCode.NotFound);
        (await scenario.Owner.PutAsync($"{appPath}/roles/editor/users/{Guid.NewGuid()}")).Expect(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("manage-users")]
    [InlineData("realm-admin")]
    public async Task EngineReservedRoleNames_AreRejected(string name)
    {
        // Keycloak only lets an admin grant these names if it holds the identically named admin role itself, so they
        // could be created but never assigned. Rejecting them up front avoids a role that silently cannot be used.
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var appPath = $"{scenario.Development}/applications/{created["application"]!["id"]!.GetValue<string>()}";

        var response = (await scenario.Owner.PostAsync($"{appPath}/roles", new { name })).Expect(HttpStatusCode.BadRequest);
        Assert.Contains("reserved", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserSessions_CanBeListedAndRevoked()
    {
        var scenario = await Scenario.CreateAsync(api);
        var created = await scenario.CreateApplicationAsync("spa");
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var issuer = new Uri(StackSettings.Current.KeycloakUrl, $"realms/{realm}");
        var user = TestUser.Generate();
        var userId = await KeycloakAdmin.Client.CreateUserAsync(realm, user.Email, user.Password, true, CancellationToken.None);
        using var client = new OidcClient(issuer, created["application"]!["clientId"]!.GetValue<string>(), null, TestRealm.RedirectUri);
        var tokens = await SignIn.AsAsync(user, client);

        var found = (await scenario.Owner.GetAsync($"{scenario.Development}/users?email={Uri.EscapeDataString(user.Email)}")).Expect(HttpStatusCode.OK).Json.AsArray();
        Assert.Equal(userId, Assert.Single(found)!["id"]!.GetValue<string>());

        var sessions = (await scenario.Owner.GetAsync($"{scenario.Development}/users/{userId}/sessions")).Expect(HttpStatusCode.OK).Json.AsArray();
        Assert.Single(sessions);

        (await scenario.Owner.DeleteAsync($"{scenario.Development}/users/{userId}/sessions")).Expect(HttpStatusCode.NoContent);
        using var refresh = await client.TryRefreshAsync(tokens.RefreshToken!);
        Assert.False(refresh.IsSuccessStatusCode);
    }
}
