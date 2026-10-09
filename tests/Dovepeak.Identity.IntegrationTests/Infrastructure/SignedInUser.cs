namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

public static class SignIn
{
    /// <summary>Creates a verified user and completes a full Authorization Code + PKCE sign-in for the client.</summary>
    public static async Task<(TestUser User, TokenResponse Tokens)> NewUserAsync(TestRealm realm, OidcClient client)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(client);

        var user = await realm.CreateUserAsync();
        var tokens = await AsAsync(user, client);
        return (user, tokens);
    }

    public static async Task<TokenResponse> AsAsync(TestUser user, OidcClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = client.CreateAuthorizationRequest();
        using var browser = new BrowserSession(client.RedirectUri);
        var callback = await browser.SignInAsync(request, user);
        return await client.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);
    }
}
