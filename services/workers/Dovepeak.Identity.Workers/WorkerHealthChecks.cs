using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dovepeak.Identity.Workers;

public static class WorkerHealthChecks
{
    private const string ReadyTag = "ready";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddWorkerHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        var checks = services.AddHealthChecks().AddNpgSql(postgres, name: "postgres", tags: [ReadyTag], timeout: CheckTimeout);
        if (configuration["Keycloak:HealthUrl"] is { Length: > 0 } keycloakHealthUrl)
        {
            checks.AddUrlGroup(new Uri(keycloakHealthUrl), name: "keycloak", tags: [ReadyTag], timeout: CheckTimeout);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapWorkerHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteResponse });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag), ResponseWriter = WriteResponse });
        return endpoints;
    }

    // Status only: exception messages can contain hostnames or connection details.
    private static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        }));
    }
}
