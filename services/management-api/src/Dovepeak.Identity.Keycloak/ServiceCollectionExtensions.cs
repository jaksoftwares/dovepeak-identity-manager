using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Keycloak;

public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan AdminRequestTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddKeycloakAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();
        services.AddSingleton(_ => RealmTemplate.LoadEmbedded());
        services.AddSingleton<KeycloakAccessTokenProvider>();

        services.AddHttpClient(KeycloakAccessTokenProvider.HttpClientName, ConfigureBaseAddress);
        services.AddHttpClient<KeycloakAdminClient>(ConfigureBaseAddress);
        services.AddTransient<SigningKeyRotationService>();

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        client.BaseAddress = options.BaseUrl;
        client.Timeout = AdminRequestTimeout;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
