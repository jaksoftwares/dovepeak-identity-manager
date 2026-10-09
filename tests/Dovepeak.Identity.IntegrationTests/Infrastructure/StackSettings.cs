using Microsoft.Extensions.Configuration;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Connection settings for the running local stack. Defaults match .env.example; override with
/// DOVEPEAK_IT_* environment variables (for example in CI).
/// </summary>
public sealed class StackSettings
{
    public static StackSettings Current { get; } = Load();

    /// <summary>Public Keycloak URL, through the edge proxy. Used for all end-user and OIDC traffic.</summary>
    public required Uri KeycloakUrl { get; init; }

    /// <summary>Direct, local-only Keycloak URL for the admin API (blocked on the public edge).</summary>
    public required Uri KeycloakAdminUrl { get; init; }

    public required string ManagementClientId { get; init; }

    public required string ManagementClientSecret { get; init; }

    public required Uri MailpitUrl { get; init; }

    /// <summary>SMTP host as seen from inside the Keycloak container.</summary>
    public required string SmtpHost { get; init; }

    public required int SmtpPort { get; init; }

    public required string PostgresConnection { get; init; }

    private static StackSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "DOVEPEAK_IT_")
            .Build();

        return new StackSettings
        {
            KeycloakUrl = new Uri(configuration["KEYCLOAK_URL"] ?? "http://localhost:8080/"),
            KeycloakAdminUrl = new Uri(configuration["KEYCLOAK_ADMIN_URL"] ?? "http://localhost:8081/"),
            ManagementClientId = configuration["MANAGEMENT_CLIENT_ID"] ?? "dovepeak-management",
            ManagementClientSecret = configuration["MANAGEMENT_CLIENT_SECRET"] ?? "management_client_local_only",
            MailpitUrl = new Uri(configuration["MAILPIT_URL"] ?? "http://localhost:8025/"),
            SmtpHost = configuration["SMTP_HOST"] ?? "mailpit",
            SmtpPort = int.Parse(configuration["SMTP_PORT"] ?? "1025", System.Globalization.CultureInfo.InvariantCulture),
            PostgresConnection = configuration["POSTGRES_CONNECTION"]
                ?? "Host=localhost;Port=5442;Database=dovepeak;Username=dovepeak;Password=dovepeak_local_only",
        };
    }
}
