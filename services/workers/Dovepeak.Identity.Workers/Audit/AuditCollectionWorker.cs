using System.ComponentModel.DataAnnotations;
using Dovepeak.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Workers.Audit;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Audit events older than this are deleted (problem statement §11: retention policy).</summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "3650.00:00:00")]
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(365);
}

/// <summary>Runs audit collection on a fixed interval and applies the retention policy hourly.</summary>
public sealed partial class AuditCollectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditCollectionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        var nextRetention = DateTimeOffset.MinValue;

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AuditEventCollector>().CollectAllAsync(stoppingToken);

                if (timeProvider.GetUtcNow() >= nextRetention)
                {
                    await ApplyRetentionAsync(scope.ServiceProvider.GetRequiredService<PlatformDbContext>(), stoppingToken);
                    nextRetention = timeProvider.GetUtcNow() + RetentionInterval;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep running: the next tick retries. Persistent failures are visible in logs and metrics.
                LogCycleFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ApplyRetentionAsync(PlatformDbContext db, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - options.Value.Retention;
        var deleted = await db.AuditEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            LogRetention(logger, deleted, cutoff);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit collection cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} audit events older than {Cutoff}")]
    private static partial void LogRetention(ILogger logger, int count, DateTimeOffset cutoff);
}
