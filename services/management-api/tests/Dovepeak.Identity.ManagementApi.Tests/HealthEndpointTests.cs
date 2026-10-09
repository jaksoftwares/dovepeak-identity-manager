using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dovepeak.Identity.ManagementApi.Tests;

public sealed class HealthEndpointTests : IClassFixture<HealthEndpointTests.UnreachableDependenciesFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(UnreachableDependenciesFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_IsHealthy_EvenWhenDependenciesAreUnreachable()
    {
        var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_IsUnavailable_WhenDependenciesAreUnreachable()
    {
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Unhealthy", checks.GetProperty("postgres").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("redis").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("keycloak").GetString());
    }

    [Fact]
    public async Task Readiness_DoesNotLeakConnectionDetails()
    {
        var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var content = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("127.0.0.1", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", content, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class UnreachableDependenciesFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Port 1 is reserved and nothing listens on it, so every dependency check fails fast.
            builder.UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=not-a-secret;Timeout=2");
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false,connectTimeout=1000");
            builder.UseSetting("Keycloak:HealthUrl", "http://127.0.0.1:1/health/ready");
            builder.UseSetting("Keycloak:BaseUrl", "http://127.0.0.1:1");
            builder.UseSetting("Keycloak:ClientId", "test-client");
            builder.UseSetting("Keycloak:ClientSecret", "not-a-secret");
        }
    }
}
