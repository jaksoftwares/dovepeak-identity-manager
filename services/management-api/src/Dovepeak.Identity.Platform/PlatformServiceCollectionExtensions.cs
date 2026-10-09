using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Platform.Applications;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Credentials;
using Dovepeak.Identity.Platform.Organizations;
using Dovepeak.Identity.Platform.Outbox;
using Dovepeak.Identity.Platform.Projects;
using Dovepeak.Identity.Platform.Reconciliation;
using Dovepeak.Identity.Platform.Security;
using Dovepeak.Identity.Platform.Webhooks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform application layer. The host must register an <see cref="ICallerAccessor"/>;
    /// <see cref="SystemCallerAccessor"/> is used when none is registered (background workers).
    /// </summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PlatformOptions>()
            .Bind(configuration.GetSection(PlatformOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddKeycloakAdmin(configuration);
        services.AddPlatformPersistence(configuration);

        // Shared key ring: webhook secrets are encrypted by the API and decrypted by workers.
        services.AddDataProtection()
            .SetApplicationName("dovepeak-identity")
            .PersistKeysToDbContext<PlatformDbContext>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICallerAccessor, SystemCallerAccessor>();

        services.AddScoped<TenantAuthorizer>();
        services.AddScoped<ManagementAuditLog>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<RoleService>();
        services.AddScoped<ScopeService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<ApiKeyValidator>();
        services.AddScoped<WebhookService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<PlatformRealmInitializer>();
        services.AddSingleton<ApiKeyCodec>();

        services.AddSingleton<OutboxProcessor>();
        services.AddSingleton<ReconciliationService>();
        services.AddSingleton<WebhookDispatcher>();
        services.AddHttpClient(WebhookDispatcher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(provider => WebhookDispatcher.CreateHandler(provider.GetRequiredService<IOptions<PlatformOptions>>()));

        return services;
    }
}
