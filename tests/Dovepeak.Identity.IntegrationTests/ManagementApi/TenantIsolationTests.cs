extern alias ManagementApi;

using System.Net;
using System.Text.RegularExpressions;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>
/// Milestones M3.2 and M3.9 — the tenant-isolation suite. Every organization-scoped endpoint is discovered from the
/// running API and called by a fully privileged member (and an API key) of organization A, using the real IDs of
/// organization B's resources. Every call must return 404: never success, never 403 (which would confirm existence),
/// never 500. New endpoints are covered automatically.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed partial class TenantIsolationTests(ManagementApiFixture api)
{
    /// <summary>Endpoints outside /v1/organizations/{orgId}. A new unscoped endpoint must be added here deliberately.</summary>
    private static readonly HashSet<string> UnscopedEndpoints =
    [
        "GET /v1/me",
        "GET /v1/me/invitations",
        "POST /v1/me/invitations/{invitationId:guid}/accept",
        "GET /v1/organizations",
        "POST /v1/organizations",
    ];

    [Fact]
    public void EveryTenantEndpoint_IsOrganizationScoped()
    {
        var endpoints = ApiEndpoints().ToList();
        var v1 = endpoints.Where(e => e.Route.StartsWith("/v1/", StringComparison.Ordinal)).ToList();

        var unscoped = v1.Where(e => !e.OrganizationScoped).Select(e => $"{e.Method} {e.Route}").ToHashSet();
        Assert.Equal(UnscopedEndpoints.Order(), unscoped.Order());

        Assert.All(v1.Where(e => e.OrganizationScoped), e => Assert.StartsWith("/v1/organizations/{orgId:guid}", e.Route, StringComparison.Ordinal));
        Assert.True(v1.Count(e => e.OrganizationScoped) >= 35, $"Only {v1.Count(e => e.OrganizationScoped)} organization endpoints found.");
    }

    [Fact]
    public async Task OwnerOfOrganizationA_CannotReachAnythingInOrganizationB()
    {
        var (attacker, victim) = await ArrangeAsync();
        await AssertIsolatedAsync(attacker.Owner, victim);
    }

    [Fact]
    public async Task ApiKeyOfOrganizationA_CannotReachAnythingInOrganizationB()
    {
        var (attacker, victim) = await ArrangeAsync();
        var scopes = new[]
        {
            "organization:read", "members:read", "projects:read", "projects:write", "projects:delete", "applications:read",
            "applications:write", "credentials:manage", "users:manage", "audit:read", "webhooks:manage",
        };
        var key = (await attacker.Owner.PostAsync($"{attacker.Org}/api-keys", new { name = "attacker", scopes })).Expect(HttpStatusCode.Created)
            .Json["key"]!.GetValue<string>();

        using var automation = api.Client(key);
        await AssertIsolatedAsync(automation, victim);
    }

    private async Task AssertIsolatedAsync(ApiClient attacker, Victim victim)
    {
        var failures = new List<string>();
        var probed = 0;

        foreach (var endpoint in ApiEndpoints().Where(e => e.OrganizationScoped))
        {
            var path = RouteParameter().Replace(endpoint.Route, m => victim.Values[m.Groups[1].Value]);
            var response = await attacker.SendAsync(new HttpMethod(endpoint.Method), path, endpoint.Method is "GET" or "DELETE" ? null : AnyBody);
            probed++;

            if (response.Status != HttpStatusCode.NotFound)
            {
                failures.Add($"{endpoint.Method} {endpoint.Route} -> {(int)response.Status} {response.Body[..Math.Min(200, response.Body.Length)]}");
            }
        }

        Assert.True(probed >= 35);
        Assert.Empty(failures);

        // The victim's resources are intact and still reachable by the victim.
        (await victim.Owner.GetAsync($"/v1/organizations/{victim.Values["orgId"]}/projects/{victim.Values["projectId"]}")).Expect(HttpStatusCode.OK);
    }

    private async Task<(Scenario Attacker, Victim Victim)> ArrangeAsync()
    {
        var attacker = await Scenario.CreateAsync(api);
        var victim = await Scenario.CreateAsync(api);

        var application = await victim.CreateApplicationAsync("web");
        var applicationPath = $"{victim.Development}/applications/{application["application"]!["id"]!.GetValue<string>()}";
        (await victim.Owner.PostAsync($"{applicationPath}/roles", new { name = "administrator" })).Expect(HttpStatusCode.Created);
        var invitation = (await victim.Owner.PostAsync($"{victim.Org}/invitations", new { email = $"x-{Guid.NewGuid():N}@example.test", role = "viewer" }))
            .Expect(HttpStatusCode.Created).Json;
        var apiKey = (await victim.Owner.PostAsync($"{victim.Org}/api-keys", new { name = "victim", scopes = new[] { "projects:read" } }))
            .Expect(HttpStatusCode.Created).Json;
        var webhook = (await victim.Owner.PostAsync($"{victim.Org}/webhooks", new { url = "https://example.com/hook" })).Expect(HttpStatusCode.Created).Json;
        var endUserId = await KeycloakAdmin.Client.CreateUserAsync(RealmName.ForEnvironment(victim.DevelopmentId),
            $"victim-{Guid.NewGuid():N}@example.test", $"Pw-{Guid.NewGuid():N}", true, CancellationToken.None);

        var values = new Dictionary<string, string>
        {
            ["orgId"] = victim.OrganizationId.ToString(),
            ["projectId"] = victim.ProjectId.ToString(),
            ["environmentId"] = victim.DevelopmentId.ToString(),
            ["applicationId"] = application["application"]!["id"]!.GetValue<string>(),
            ["roleName"] = "administrator",
            ["invitationId"] = invitation["id"]!.GetValue<string>(),
            ["keyId"] = apiKey["apiKey"]!["id"]!.GetValue<string>(),
            ["webhookId"] = webhook["webhook"]!["id"]!.GetValue<string>(),
            ["sessionId"] = Guid.NewGuid().ToString(),
        };

        // {userId} appears on member routes (a victim member) and end-user routes (a victim end user).
        values["userId"] = victim.Owner.UserId!;
        return (attacker, new Victim(victim.Owner, values, endUserId));
    }

    /// <summary>A body with every field any endpoint accepts, so requests reach authorization rather than failing binding.</summary>
    private static readonly object AnyBody = new
    {
        slug = "probe",
        name = "Probe",
        email = "probe@example.test",
        role = "viewer",
        kind = "spa",
        redirectUris = new[] { "https://probe.example/callback" },
        scopes = new[] { "projects:read" },
        url = "https://probe.example/hook",
        eventTypes = Array.Empty<string>(),
    };

    private IEnumerable<ApiEndpoint> ApiEndpoints() =>
        api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(method => new ApiEndpoint(
                method,
                "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.Any(m => m.GetType().Name == "OrganizationScopedAttribute"))));

    [GeneratedRegex(@"\{(\w+)(?::\w+)?\}")]
    private static partial Regex RouteParameter();

    private sealed record ApiEndpoint(string Method, string Route, bool OrganizationScoped);

    private sealed record Victim(ApiClient Owner, Dictionary<string, string> Values, string EndUserId);
}
