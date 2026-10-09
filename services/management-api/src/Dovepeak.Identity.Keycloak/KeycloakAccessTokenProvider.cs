using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// Obtains and caches the Management API service account's admin token (client credentials grant).
/// </summary>
public sealed class KeycloakAccessTokenProvider(
    IHttpClientFactory httpClientFactory, IOptions<KeycloakOptions> options, TimeProvider timeProvider)
    : IDisposable
{
    public const string HttpClientName = "keycloak-token";

    // Refresh slightly early so a token never expires in flight.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private CachedToken? _cached;
    private long _generation;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetValid(out var token))
        {
            return token;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                if (TryGetValid(out token))
                {
                    return token;
                }

                // A token requested before an invalidation may predate a newly created realm and lack rights
                // over it. Such a token is never cached; the loop requests another.
                var generation = Interlocked.Read(ref _generation);
                var fetched = await FetchAsync(cancellationToken).ConfigureAwait(false);

                if (Interlocked.Read(ref _generation) == generation)
                {
                    Volatile.Write(ref _cached, fetched with { Generation = generation });
                    return fetched.Token;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Discards the cached token. Required after creating a realm: Keycloak grants the service account
    /// rights over the new realm, and those rights only appear in a token issued afterwards.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _cached, null);
    }

    public void Dispose() => _lock.Dispose();

    private bool TryGetValid(out string token)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null
            && cached.Generation == Interlocked.Read(ref _generation)
            && timeProvider.GetUtcNow() < cached.ExpiresAt)
        {
            token = cached.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> FetchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var endpoint = new Uri(settings.BaseUrl!, $"realms/{settings.AdminRealm}/protocol/openid-connect/token");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
            }),
        };

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The response body is not included: it may echo request details.
            throw new KeycloakAdminException(
                $"Failed to obtain a Keycloak admin token for client '{settings.ClientId}'.", response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new KeycloakAdminException("Keycloak returned an empty token response.", response.StatusCode);

        return new CachedToken(payload.AccessToken, timeProvider.GetUtcNow().AddSeconds(payload.ExpiresIn) - ExpiryMargin, Generation: 0);
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt, long Generation);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
