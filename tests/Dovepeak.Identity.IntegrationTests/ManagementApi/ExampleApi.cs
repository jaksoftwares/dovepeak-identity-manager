extern alias ProtectedApi;

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Hosts the example protected API (a customer's resource server) against a tenant environment's issuer.</summary>
internal static class ExampleApi
{
    public const string Audience = "dovepeak-demo-api";

    public static WebApplicationFactory<ProtectedApi::Program> For(Uri issuer) =>
        new WebApplicationFactory<ProtectedApi::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Issuer", issuer.ToString());
            builder.UseSetting("Auth:Audience", Audience);
            builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        });

    public static async Task<HttpStatusCode> CallAsync(WebApplicationFactory<ProtectedApi::Program> factory, string path, string token)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }
}
