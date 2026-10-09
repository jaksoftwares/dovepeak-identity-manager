using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Builds the production <see cref="KeycloakAdminClient"/> against the running stack, using the same
/// least-privilege service account as the Management API.
/// </summary>
public static class KeycloakAdmin
{
    private static readonly Lazy<ServiceProvider> Provider = new(Build);

    public static KeycloakAdminClient Client => Provider.Value.GetRequiredService<KeycloakAdminClient>();

    public static RealmTemplate Template => Provider.Value.GetRequiredService<RealmTemplate>();

    public static KeycloakAccessTokenProvider TokenProvider => Provider.Value.GetRequiredService<KeycloakAccessTokenProvider>();

    public static SigningKeyRotationService KeyRotation => Provider.Value.GetRequiredService<SigningKeyRotationService>();

    private static ServiceProvider Build()
    {
        var settings = StackSettings.Current;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:BaseUrl"] = settings.KeycloakAdminUrl.ToString(),
                ["Keycloak:ClientId"] = settings.ManagementClientId,
                ["Keycloak:ClientSecret"] = settings.ManagementClientSecret,
                ["Keycloak:Smtp:Host"] = settings.SmtpHost,
                ["Keycloak:Smtp:Port"] = settings.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Keycloak:Smtp:StartTls"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddKeycloakAdmin(configuration);
        return services.BuildServiceProvider();
    }
}
