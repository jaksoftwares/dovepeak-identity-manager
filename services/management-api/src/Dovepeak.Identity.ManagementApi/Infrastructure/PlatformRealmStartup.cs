using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Platform.Security;

namespace Dovepeak.Identity.ManagementApi.Infrastructure;

/// <summary>
/// Ensures the platform realm and portal client exist when the API starts. Retries with backoff so the API can start
/// before the identity engine is ready; readiness probes report the dependency state meanwhile.
/// </summary>
internal sealed partial class PlatformRealmStartup(IServiceScopeFactory scopeFactory, ILogger<PlatformRealmStartup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PlatformRealmInitializer>().EnsureAsync(stoppingToken);
                LogReady(logger);
                return;
            }
            catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException)
            {
                LogRetry(logger, ex, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Platform realm is ready")]
    private static partial void LogReady(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Platform realm setup failed; retrying in {Delay}")]
    private static partial void LogRetry(ILogger logger, Exception exception, TimeSpan delay);
}
