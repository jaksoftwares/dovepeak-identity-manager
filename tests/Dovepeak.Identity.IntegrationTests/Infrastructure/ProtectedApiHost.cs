extern alias ProtectedApi;

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the example protected API in-process, trusting the given realm. Each instance starts with an empty
/// JWKS cache, like a resource server that has just refreshed its signing keys.
/// </summary>
public sealed class ProtectedApiHost : IDisposable
{
    private readonly WebApplicationFactory<ProtectedApi::Program> _factory;
    private readonly HttpClient _client;

    public ProtectedApiHost(TestRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        _factory = new WebApplicationFactory<ProtectedApi::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Issuer", realm.Issuer.ToString());
            builder.UseSetting("Auth:Audience", TestRealm.ApiAudience);
            builder.UseSetting("Auth:RequireHttpsMetadata", "false");
            builder.UseSetting("Auth:ClockSkewSeconds", "0");
        });
        _client = _factory.CreateClient();
    }

    public async Task<HttpStatusCode> CallMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _client.SendAsync(request);
        return response.StatusCode;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
