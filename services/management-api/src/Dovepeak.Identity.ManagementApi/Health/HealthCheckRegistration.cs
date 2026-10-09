using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dovepeak.Identity.ManagementApi.Health;

public static class HealthCheckRegistration
{
    public const string ReadyTag = "ready";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddPlatformHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
        var keycloakHealthUrl = configuration["Keycloak:HealthUrl"]
            ?? throw new InvalidOperationException("Setting 'Keycloak:HealthUrl' is not configured.");

        services.AddHealthChecks()
            .AddNpgSql(postgres, name: "postgres", tags: [ReadyTag], timeout: CheckTimeout)
            .AddRedis(redis, name: "redis", tags: [ReadyTag], timeout: CheckTimeout)
            .AddUrlGroup(new Uri(keycloakHealthUrl), name: "keycloak", tags: [ReadyTag], timeout: CheckTimeout);

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is running. Never depends on external services,
        // so a dependency outage does not cause orchestrators to restart the API.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        // Readiness: the API can serve traffic because its dependencies are reachable.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        return endpoints;
    }

    // Reports only status per dependency. Exception messages and descriptions are
    // deliberately omitted because they can contain hostnames or connection details.
    private static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
