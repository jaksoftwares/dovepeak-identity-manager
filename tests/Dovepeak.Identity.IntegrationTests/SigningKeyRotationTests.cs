using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M2.3 — planned rotation, retirement and emergency rotation of signing keys.</summary>
[Trait("Category", "Integration")]
public sealed class SigningKeyRotationTests
{
    [Fact]
    public async Task PlannedRotation_NewTokensUseNewKey_AndOldTokensStillValidate()
    {
        await using var realm = await IsolatedRealm.CreateAsync();
        var (user, before) = await SignIn.NewUserAsync(realm.Value, realm.Value.WebApp);

        await KeycloakAdmin.KeyRotation.RotateAsync(realm.Value.Name, CancellationToken.None);
        var after = await SignIn.AsAsync(user, realm.Value.WebApp);

        var oldKid = KeyId(before.AccessToken);
        var newKid = KeyId(after.AccessToken);
        Assert.NotEqual(oldKid, newKid);

        var published = await PublishedKeyIdsAsync(realm.Value);
        Assert.Contains(oldKid, published);
        Assert.Contains(newKid, published);

        using var api = new ProtectedApiHost(realm.Value);
        Assert.Equal(HttpStatusCode.OK, await api.CallMeAsync(before.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await api.CallMeAsync(after.AccessToken));
    }

    [Fact]
    public async Task Retirement_RespectsMinimumPassiveAge_ThenRemovesOldKey()
    {
        await using var realm = await IsolatedRealm.CreateAsync();
        var (user, before) = await SignIn.NewUserAsync(realm.Value, realm.Value.WebApp);
        await KeycloakAdmin.KeyRotation.RotateAsync(realm.Value.Name, CancellationToken.None);
        var after = await SignIn.AsAsync(user, realm.Value.WebApp);

        // Too soon: tokens signed by the old key may still be valid.
        Assert.Empty(await KeycloakAdmin.KeyRotation.RetirePassiveKeysAsync(realm.Value.Name, TimeSpan.FromMinutes(15), CancellationToken.None));

        Assert.Single(await KeycloakAdmin.KeyRotation.RetirePassiveKeysAsync(realm.Value.Name, TimeSpan.Zero, CancellationToken.None));
        Assert.DoesNotContain(KeyId(before.AccessToken), await PublishedKeyIdsAsync(realm.Value));

        using var api = new ProtectedApiHost(realm.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, await api.CallMeAsync(before.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await api.CallMeAsync(after.AccessToken));
    }

    [Fact]
    public async Task EmergencyRotation_InvalidatesEveryOutstandingToken()
    {
        await using var realm = await IsolatedRealm.CreateAsync();
        var (user, compromised) = await SignIn.NewUserAsync(realm.Value, realm.Value.WebApp);

        await KeycloakAdmin.KeyRotation.EmergencyRotateAsync(realm.Value.Name, CancellationToken.None);

        using var refresh = await realm.Value.WebApp.TryRefreshAsync(compromised.RefreshToken!);
        Assert.False(refresh.IsSuccessStatusCode);
        Assert.DoesNotContain(KeyId(compromised.AccessToken), await PublishedKeyIdsAsync(realm.Value));

        using var api = new ProtectedApiHost(realm.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, await api.CallMeAsync(compromised.AccessToken));

        // Users can sign in again immediately with the new keys.
        var fresh = await SignIn.AsAsync(user, realm.Value.WebApp);
        Assert.Equal(HttpStatusCode.OK, await api.CallMeAsync(fresh.AccessToken));
    }

    private static string KeyId(string jwt) => new JwtSecurityToken(jwt).Header.Kid;

    private static async Task<IReadOnlyList<string>> PublishedKeyIdsAsync(TestRealm realm)
    {
        using var http = new HttpClient();
        var jwks = await http.GetFromJsonAsync<JsonObject>(new Uri($"{realm.Issuer}/protocol/openid-connect/certs"));
        return jwks!["keys"]!.AsArray().Select(k => k!["kid"]!.GetValue<string>()).ToList();
    }

    private sealed class IsolatedRealm : IAsyncDisposable
    {
        private IsolatedRealm(TestRealm value) => Value = value;

        public TestRealm Value { get; }

        public static async Task<IsolatedRealm> CreateAsync()
        {
            var realm = new TestRealm();
            await realm.InitializeAsync();
            return new IsolatedRealm(realm);
        }

        public async ValueTask DisposeAsync() => await Value.DisposeAsync();
    }
}
