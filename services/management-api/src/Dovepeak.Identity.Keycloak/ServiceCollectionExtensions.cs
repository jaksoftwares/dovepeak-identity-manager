using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Keycloak;

public static class ServiceCollectionExtensions
{
    // Realm creation is the slowest admin call and grows with tenant count (Phase 1 validation report), so a single
    // attempt may take up to 30 seconds; retries stay within one minute in total.
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddKeycloakAdmin(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();
        services.AddSingleton(_ => RealmTemplate.LoadEmbedded());
        services.AddSingleton<KeycloakAccessTokenProvider>();

        services.AddHttpClient(KeycloakAccessTokenProvider.HttpClientName, ConfigureBaseAddress)
            .AddHttpMessageHandler(() => new ResilienceExceptionHandler())
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddHttpClient<KeycloakAdminClient>(ConfigureBaseAddress)
            .AddHttpMessageHandler(() => new ResilienceExceptionHandler())
            .AddStandardResilienceHandler(ConfigureResilience);
        services.AddTransient<SigningKeyRotationService>();

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        client.BaseAddress = options.BaseUrl;

        // Timeouts are enforced by the resilience pipeline, per attempt and in total.
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// Bounded retries for transient failures (5xx, 408, 429, connection errors) on idempotent requests only: a retried
    /// POST could create a realm, client or user twice. The circuit breaker stops hammering an engine that is down.
    /// </summary>
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalTimeout;
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.SamplingDuration = AttemptTimeout * 2;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
