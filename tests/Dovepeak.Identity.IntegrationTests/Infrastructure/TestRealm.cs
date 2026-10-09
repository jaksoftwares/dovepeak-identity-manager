using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// A disposable tenant realm provisioned through the production provisioning code, with standard test clients.
/// One instance is shared by all tests in a class (xUnit class fixture) and deleted afterwards.
/// </summary>
public class TestRealm : IAsyncLifetime
{
    public const string ApiAudience = "dovepeak-demo-api";

    public static readonly Uri RedirectUri = new("http://localhost:3999/callback");
    public static readonly Uri PostLogoutRedirectUri = new("http://localhost:3999/");
    public static readonly Uri AppOrigin = new("http://localhost:3999/");

    public RealmName Name { get; } = RealmName.Parse($"it-{Guid.NewGuid():N}");

    public static KeycloakAdminClient Admin => KeycloakAdmin.Client;

    public Uri Issuer => new(StackSettings.Current.KeycloakUrl, $"realms/{Name}");

    /// <summary>Public SPA-style client whose tokens carry the demo API audience.</summary>
    public OidcClient WebApp { get; private set; } = null!;

    /// <summary>Public client without the API audience (for audience validation tests).</summary>
    public OidcClient NoAudienceApp { get; private set; } = null!;

    /// <summary>Confidential server-side client (BFF pattern).</summary>
    public OidcClient Bff { get; private set; } = null!;

    public string BffSecret { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await Admin.CreateRealmAsync(Name, "Integration Test Realm", CancellationToken.None);

        await Admin.CreateClientAsync(Name, Interactive("web-app", ClientKind.Public, withAudience: true), CancellationToken.None);
        await Admin.CreateClientAsync(Name, Interactive("no-audience-app", ClientKind.Public, withAudience: false), CancellationToken.None);
        var bff = await Admin.CreateClientAsync(Name, Interactive("bff", ClientKind.Confidential, withAudience: true), CancellationToken.None);
        BffSecret = bff.Secret!;

        WebApp = new OidcClient(Issuer, "web-app", clientSecret: null, RedirectUri);
        NoAudienceApp = new OidcClient(Issuer, "no-audience-app", clientSecret: null, RedirectUri);
        Bff = new OidcClient(Issuer, "bff", BffSecret, RedirectUri);
    }

    public virtual async Task DisposeAsync()
    {
        await Admin.DeleteRealmAsync(Name, CancellationToken.None);
        WebApp?.Dispose();
        NoAudienceApp?.Dispose();
        Bff?.Dispose();
    }

    public async Task<TestUser> CreateUserAsync(bool emailVerified = true)
    {
        var user = TestUser.Generate();
        var id = await Admin.CreateUserAsync(Name, user.Email, user.Password, emailVerified, CancellationToken.None);
        return user with { Id = id };
    }

    private static ClientRegistration Interactive(string clientId, ClientKind kind, bool withAudience) =>
        new(clientId, kind)
        {
            RedirectUris = [RedirectUri],
            PostLogoutRedirectUris = [PostLogoutRedirectUri],
            WebOrigins = [AppOrigin],
            Audiences = withAudience ? [ApiAudience] : [],
        };
}

public sealed record TestUser(string Email, string Password)
{
    public string? Id { get; init; }

    public static TestUser Generate() =>
        new($"user-{Guid.NewGuid():N}@example.test", $"Pw-{Guid.NewGuid():N}");
}
