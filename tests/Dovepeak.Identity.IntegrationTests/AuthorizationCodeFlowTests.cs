using System.IdentityModel.Tokens.Jwt;
using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M1.2 — Authorization Code with PKCE for public and confidential (BFF) clients.</summary>
[Trait("Category", "Integration")]
public sealed class AuthorizationCodeFlowTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task PublicClient_SignsIn_WithPkce()
    {
        var user = await realm.CreateUserAsync();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var callback = await browser.SignInAsync(request, user);
        var tokens = await realm.WebApp.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);

        var access = new JwtSecurityToken(tokens.AccessToken);
        var id = new JwtSecurityToken(tokens.IdToken);
        Assert.Equal(realm.Issuer.ToString(), access.Issuer);
        Assert.Contains(TestRealm.ApiAudience, access.Audiences);
        Assert.Equal(user.Id, access.Subject);
        Assert.Equal(request.Nonce, id.Claims.Single(c => c.Type == "nonce").Value);
        Assert.Equal(user.Email, id.Claims.Single(c => c.Type == "email").Value);
        Assert.InRange(tokens.ExpiresIn, 590, 600);
        Assert.NotNull(tokens.RefreshToken);
    }

    [Fact]
    public async Task ConfidentialClient_SignsIn_WithPkceAndSecret()
    {
        var user = await realm.CreateUserAsync();
        var request = realm.Bff.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var callback = await browser.SignInAsync(request, user);
        var tokens = await realm.Bff.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);

        Assert.Equal("bff", new JwtSecurityToken(tokens.AccessToken).Claims.Single(c => c.Type == "azp").Value);
    }

    [Fact]
    public async Task CodeExchange_WithWrongVerifier_IsRejected()
    {
        var user = await realm.CreateUserAsync();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        var callback = await browser.SignInAsync(request, user);

        var exchange = () => realm.WebApp.ExchangeCodeAsync(callback.RequireCode(request), "wrong-verifier-" + Guid.NewGuid());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(exchange);
        Assert.Contains("invalid_grant", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationCode_CanOnlyBeUsedOnce()
    {
        var user = await realm.CreateUserAsync();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        var code = (await browser.SignInAsync(request, user)).RequireCode(request);

        await realm.WebApp.ExchangeCodeAsync(code, request.CodeVerifier);
        var replay = () => realm.WebApp.ExchangeCodeAsync(code, request.CodeVerifier);

        await Assert.ThrowsAsync<InvalidOperationException>(replay);
    }

    [Fact]
    public async Task WrongPassword_ShowsGenericError_AndNoRedirect()
    {
        var user = await realm.CreateUserAsync();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var page = (await browser.GetAsync(request.Url)).RequirePage();
        var result = await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = user.Email,
            ["password"] = "not-the-password",
        });

        Assert.Null(result.CallbackUri);
        Assert.Contains("Invalid username or password", result.Page!.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownUser_GetsSameErrorAsWrongPassword()
    {
        // Threat model I-02: login responses must not reveal whether an account exists.
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var page = (await browser.GetAsync(request.Url)).RequirePage();
        var result = await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = $"nobody-{Guid.NewGuid():N}@example.test",
            ["password"] = "irrelevant-password",
        });

        Assert.Contains("Invalid username or password", result.Page!.Errors, StringComparison.Ordinal);
    }
}
