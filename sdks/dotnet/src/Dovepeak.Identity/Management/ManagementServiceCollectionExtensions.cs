using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.Management;

/// <summary>Registers <see cref="DovepeakManagementClient"/> with <c>IHttpClientFactory</c>.</summary>
public static class ManagementServiceCollectionExtensions
{
    /// <summary>Adds the Management API client, configured from a section with <c>BaseUrl</c> and <c>ApiKey</c>.</summary>
    public static IHttpClientBuilder AddDovepeakManagementClient(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDovepeakManagementClient(options => configuration.Bind(options));

    /// <summary>Adds the Management API client.</summary>
    public static IHttpClientBuilder AddDovepeakManagementClient(this IServiceCollection services, Action<DovepeakManagementOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<DovepeakManagementOptions>()
            .Configure(configure)
            .Validate(o => o.BaseUrl is not null && !string.IsNullOrWhiteSpace(o.ApiKey), "Dovepeak management client: BaseUrl and ApiKey are required.")
            .ValidateOnStart();

        return services.AddHttpClient<DovepeakManagementClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
    }
}
