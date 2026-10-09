using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M1.1 — realm and client provisioning through the least-privilege service account.</summary>
[Trait("Category", "Integration")]
public sealed class RealmProvisioningTests
{
    private static KeycloakAdminClient Admin => KeycloakAdmin.Client;

    [Fact]
    public async Task CreateRealm_IsIdempotent_AndDeleteRemovesIt()
    {
        var realm = RealmName.Parse($"it-{Guid.NewGuid():N}");

        Assert.Equal(ProvisioningOutcome.Created, await Admin.CreateRealmAsync(realm, "Provisioning Test", CancellationToken.None));
        Assert.Equal(ProvisioningOutcome.AlreadyExists, await Admin.CreateRealmAsync(realm, "Provisioning Test", CancellationToken.None));
        Assert.NotNull(await Admin.GetRealmAsync(realm, CancellationToken.None));

        Assert.True(await Admin.DeleteRealmAsync(realm, CancellationToken.None));
        Assert.False(await Admin.DeleteRealmAsync(realm, CancellationToken.None));
        Assert.Null(await Admin.GetRealmAsync(realm, CancellationToken.None));
    }

    [Fact]
    public async Task ServiceAccount_CannotSeeRealmsItDidNotCreate()
    {
        // Threat model E-01: the Management API's account only manages realms it provisioned.
        Assert.Null(await Admin.GetRealmAsync(RealmName.Parse("it-not-owned-by-anyone"), CancellationToken.None));
        Assert.DoesNotContain("master", await Admin.ListRealmNamesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ServiceAccountToken_HasConstantSize_WithoutRoleClaims()
    {
        // Phase 1 finding F-4: per-realm admin roles in the token would grow it ~475 bytes per tenant until it
        // exceeds HTTP header limits. Keycloak evaluates admin rights from stored role mappings instead.
        KeycloakAdmin.TokenProvider.Invalidate();
        var token = await KeycloakAdmin.TokenProvider.GetTokenAsync(CancellationToken.None);
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token).Claims.Select(c => c.Type).ToHashSet();

        Assert.DoesNotContain("resource_access", claims);
        Assert.DoesNotContain("realm_access", claims);
        Assert.True(token.Length < 2048, $"Admin token is {token.Length} bytes.");
    }

    [Fact]
    public async Task ConfidentialClient_SecretIsReturnedOnlyOnCreation()
    {
        await using var realm = await ProvisionAsync();
        var registration = new ClientRegistration("backend", ClientKind.Confidential)
        {
            RedirectUris = [new Uri("https://app.example.com/callback")],
        };

        var created = await Admin.CreateClientAsync(realm.Name, registration, CancellationToken.None);
        var again = await Admin.CreateClientAsync(realm.Name, registration, CancellationToken.None);

        Assert.Equal(ProvisioningOutcome.Created, created.Outcome);
        Assert.False(string.IsNullOrEmpty(created.Secret));
        Assert.Equal(ProvisioningOutcome.AlreadyExists, again.Outcome);
        Assert.Null(again.Secret);
        Assert.Equal(created.Id, again.Id);
    }

    [Fact]
    public async Task Clients_AreCreatedWithSecureDefaults()
    {
        await using var realm = await ProvisionAsync();
        var spa = await Admin.CreateClientAsync(realm.Name, new ClientRegistration("spa", ClientKind.Public)
        {
            RedirectUris = [new Uri("https://app.example.com/callback")],
            WebOrigins = [new Uri("https://app.example.com")],
        }, CancellationToken.None);

        var client = await Admin.GetClientAsync(realm.Name, spa.Id, CancellationToken.None);

        Assert.True(client["publicClient"]!.GetValue<bool>());
        Assert.False(client["implicitFlowEnabled"]!.GetValue<bool>());
        Assert.False(client["directAccessGrantsEnabled"]!.GetValue<bool>());
        Assert.False(client["fullScopeAllowed"]!.GetValue<bool>());
        Assert.Equal("S256", client["attributes"]!["pkce.code.challenge.method"]!.GetValue<string>());
        Assert.Equal(["https://app.example.com/callback"], client["redirectUris"]!.AsArray().Select(u => u!.GetValue<string>()));
        Assert.Equal(["https://app.example.com"], client["webOrigins"]!.AsArray().Select(u => u!.GetValue<string>()));
    }

    [Theory]
    [InlineData("https://app.example.com/*")]
    [InlineData("http://app.example.com/callback")]
    [InlineData("https://app.example.com/callback#fragment")]
    [InlineData("javascript:alert(1)")]
    public void Registration_RejectsUnsafeRedirectUris(string redirectUri)
    {
        var registration = new ClientRegistration("app", ClientKind.Public) { RedirectUris = [new Uri(redirectUri)] };
        Assert.Throws<ArgumentException>(registration.Validate);
    }

    [Theory]
    [InlineData("https://app.example.com/callback")]
    [InlineData("http://localhost:3000/callback")]
    [InlineData("http://127.0.0.1:8080/callback")]
    [InlineData("com.example.app:/oauth/callback")]
    public void Registration_AcceptsSafeRedirectUris(string redirectUri)
    {
        var registration = new ClientRegistration("app", ClientKind.Public) { RedirectUris = [new Uri(redirectUri)] };
        registration.Validate();
    }

    private static async Task<OwnedRealm> ProvisionAsync()
    {
        var name = RealmName.Parse($"it-{Guid.NewGuid():N}");
        await Admin.CreateRealmAsync(name, "Provisioning Test", CancellationToken.None);
        return new OwnedRealm(name);
    }

    private sealed record OwnedRealm(RealmName Name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Admin.DeleteRealmAsync(Name, CancellationToken.None);
    }
}
