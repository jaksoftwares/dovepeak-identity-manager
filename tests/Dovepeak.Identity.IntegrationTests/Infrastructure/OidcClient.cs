using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Minimal OAuth 2.0 / OIDC relying party for tests: builds PKCE authorization requests and calls the token endpoint.
/// </summary>
public sealed class OidcClient(Uri issuer, string clientId, string? clientSecret, Uri redirectUri) : IDisposable
{
    private readonly HttpClient _http = new();

    public Uri Issuer { get; } = issuer;

    public string ClientId { get; } = clientId;

    public Uri RedirectUri { get; } = redirectUri;

    private Uri Endpoint(string name) => new($"{Issuer}/protocol/openid-connect/{name}");

    public AuthorizationRequest CreateAuthorizationRequest(string scope = "openid email profile", bool includePkce = true)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["redirect_uri"] = RedirectUri.ToString(),
            ["state"] = state,
            ["nonce"] = nonce,
        };

        if (includePkce)
        {
            query["code_challenge"] = challenge;
            query["code_challenge_method"] = "S256";
        }

        var url = new Uri($"{Endpoint("auth")}?{string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}");
        return new AuthorizationRequest(url, verifier, state, nonce);
    }

    public async Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier)
    {
        using var response = await PostTokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri.ToString(),
            ["code_verifier"] = codeVerifier,
        });

        return await ReadTokensAsync(response);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken)
    {
        using var response = await TryRefreshAsync(refreshToken);
        return await ReadTokensAsync(response);
    }

    /// <summary>Calls the refresh grant and returns the raw response, for asserting on failures.</summary>
    public Task<HttpResponseMessage> TryRefreshAsync(string refreshToken) =>
        PostTokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

    public Task<HttpResponseMessage> PostTokenAsync(Dictionary<string, string> form) =>
        PostAsync(Endpoint("token"), form);

    /// <summary>Back-channel logout of the session that owns the refresh token.</summary>
    public async Task LogoutAsync(string refreshToken)
    {
        using var response = await PostAsync(Endpoint("logout"), new() { ["refresh_token"] = refreshToken });
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> IntrospectAsync(string token, string resourceClientId, string resourceSecret)
    {
        var form = new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = resourceClientId,
            ["client_secret"] = resourceSecret,
        };

        using var content = new FormUrlEncodedContent(form);
        return await _http.PostAsync(Endpoint("token/introspect"), content);
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> PostAsync(Uri endpoint, Dictionary<string, string> form)
    {
        form["client_id"] = ClientId;
        if (clientSecret is not null)
        {
            form["client_secret"] = clientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        return await _http.PostAsync(endpoint, content);
    }

    private static async Task<TokenResponse> ReadTokensAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {body}");
        }

        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }
}

public sealed record AuthorizationRequest(Uri Url, string CodeVerifier, string State, string Nonce);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_expires_in")] int RefreshExpiresIn);
