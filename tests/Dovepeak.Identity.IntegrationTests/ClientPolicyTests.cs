using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// Milestone M2.1 — realm-wide client policies enforce the platform baseline even for clients that were
/// configured unsafely (threat model S-03, S-05).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ClientPolicyTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task AuthorizationRequest_WithoutPkce_IsRejected()
    {
        var request = realm.WebApp.CreateAuthorizationRequest(includePkce: false);
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var callback = (await browser.GetAsync(request.Url)).RequireCallback();

        Assert.Equal("invalid_request", callback.Parameter("error"));
    }

    [Fact]
    public async Task AuthorizationRequest_WithUnregisteredRedirectUri_IsRejectedWithoutRedirect()
    {
        // An open redirect would send the user (and code) to an attacker. Keycloak must show an error page instead.
        var request = realm.WebApp.CreateAuthorizationRequest();
        var tampered = new Uri(request.Url.ToString().Replace(
            Uri.EscapeDataString(TestRealm.RedirectUri.ToString()),
            Uri.EscapeDataString("https://attacker.example/callback"),
            StringComparison.Ordinal));
        using var browser = new BrowserSession(new Uri("https://attacker.example/callback"));

        var navigation = await browser.GetAsync(tampered);

        Assert.Null(navigation.CallbackUri);
        Assert.Equal(HttpStatusCode.BadRequest, navigation.Page!.StatusCode);
    }

    [Fact]
    public async Task PasswordGrant_IsRejected()
    {
        var user = await realm.CreateUserAsync();
        await TestRealm.Admin.CreateClientAsync(realm.Name, new ClientRegistration("ropc-attempt", ClientKind.Public)
        {
            RedirectUris = [TestRealm.RedirectUri],
        }, CancellationToken.None);
        using var client = new OidcClient(realm.Issuer, "ropc-attempt", null, TestRealm.RedirectUri);

        using var response = await client.PostTokenAsync(new()
        {
            ["grant_type"] = "password",
            ["username"] = user.Email,
            ["password"] = user.Password,
        });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("not allowed for direct access grants", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImplicitFlow_IsRejected()
    {
        var request = realm.WebApp.CreateAuthorizationRequest();
        var implicitUrl = new Uri(request.Url.ToString().Replace("response_type=code", "response_type=token", StringComparison.Ordinal));
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var navigation = await browser.GetAsync(implicitUrl);

        // Rejected either with an error redirect or an error page, but never with a token.
        Assert.DoesNotContain("access_token", navigation.CallbackUri?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.True(navigation.CallbackUri?.ToString().Contains("error=", StringComparison.Ordinal) ?? navigation.Page!.StatusCode == HttpStatusCode.BadRequest);
    }
}
