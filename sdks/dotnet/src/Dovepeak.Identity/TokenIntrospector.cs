using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity;

/// <summary>Asks the identity provider whether a token is still active (RFC 7662), with a short cache.</summary>
internal sealed class TokenIntrospector(HttpClient http, IOptions<DovepeakAuthenticationOptions> options, IMemoryCache cache)
{
    public async Task<bool> IsActiveAsync(string token, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cacheKey = "dovepeak:introspection:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (settings.Introspection.CacheSeconds > 0 && cache.TryGetValue(cacheKey, out bool cached))
        {
            return cached;
        }

        var endpoint = new Uri($"{settings.Issuer.TrimEnd('/')}/protocol/openid-connect/token/introspect");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = settings.Introspection.ClientId!,
            ["client_secret"] = settings.Introspection.ClientSecret!,
        });
        using var response = await http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IntrospectionResult>(cancellationToken).ConfigureAwait(false);
        var active = result?.Active == true;

        // Only positive answers are cached: a revoked token must be rejected from the next request on.
        if (active && settings.Introspection.CacheSeconds > 0)
        {
            cache.Set(cacheKey, true, TimeSpan.FromSeconds(settings.Introspection.CacheSeconds));
        }

        return active;
    }

    private sealed record IntrospectionResult([property: JsonPropertyName("active")] bool Active);
}
