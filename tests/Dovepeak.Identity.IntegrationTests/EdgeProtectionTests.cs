using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M2.4 — the public edge hides administrative surfaces and adds security headers.</summary>
[Trait("Category", "Integration")]
public sealed class EdgeProtectionTests(TestRealm realm) : IClassFixture<TestRealm>, IDisposable
{
    private readonly HttpClient _public = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = StackSettings.Current.KeycloakUrl,
    };

    [Theory]
    [InlineData("admin/")]
    [InlineData("admin/realms")]
    [InlineData("realms/master/.well-known/openid-configuration")]
    [InlineData("realms/master/protocol/openid-connect/token")]
    [InlineData("metrics")]
    [InlineData("health/ready")]
    public async Task AdministrativeSurfaces_AreNotReachablePublicly(string path)
    {
        using var response = await _public.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantDiscovery_IsReachablePublicly_WithSecurityHeaders()
    {
        using var response = await _public.GetAsync(new Uri($"realms/{realm.Name}/.well-known/openid-configuration", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    public void Dispose() => _public.Dispose();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EdgeRateLimitGroup
{
    public const string Name = "Edge rate limiting (runs alone)";
}

/// <summary>
/// Milestone M2.4 — email-sending endpoints are rate-limited per IP (threat model D-02).
/// Runs in a non-parallel collection because exhausting the shared per-IP bucket would affect other tests.
/// </summary>
[Trait("Category", "Integration")]
[Collection(EdgeRateLimitGroup.Name)]
public sealed class EdgeRateLimitTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task EmailSendingEndpoint_ReturnsTooManyRequests_WhenLimitExceeded()
    {
        using var client = new HttpClient { BaseAddress = StackSettings.Current.KeycloakUrl };
        var path = new Uri($"realms/{realm.Name}/login-actions/reset-credentials", UriKind.Relative);
        var statuses = new List<HttpStatusCode>();

        // Unknown addresses: counted by the edge, but Keycloak sends no email.
        for (var i = 0; i < 200 && !statuses.Contains(HttpStatusCode.TooManyRequests); i++)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = $"nobody-{i}@example.test" });
            using var response = await client.PostAsync(path, form);
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
