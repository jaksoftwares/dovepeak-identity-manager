using System.ComponentModel.DataAnnotations;
using Dovepeak.Identity.Platform.Outbox;
using Dovepeak.Identity.Platform.Reconciliation;
using Dovepeak.Identity.Platform.Webhooks;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Workers;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan OutboxInterval { get; set; } = TimeSpan.FromSeconds(2);

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan WebhookInterval { get; set; } = TimeSpan.FromSeconds(2);

    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Runs a platform job on a fixed interval; failures are logged and retried on the next tick.</summary>
public abstract partial class IntervalJob(TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Job} run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string job);
}

public sealed class OutboxJob(OutboxProcessor processor, IOptions<JobOptions> options, TimeProvider time, ILogger<OutboxJob> logger)
    : IntervalJob(time, logger)
{
    protected override TimeSpan Interval => options.Value.OutboxInterval;

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        // Drain the backlog, then wait for the next tick.
        while (await processor.ProcessDueAsync(ct) > 0)
        {
        }
    }
}

public sealed class WebhookJob(WebhookDispatcher dispatcher, IOptions<JobOptions> options, TimeProvider time, ILogger<WebhookJob> logger)
    : IntervalJob(time, logger)
{
    protected override TimeSpan Interval => options.Value.WebhookInterval;

    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        while (await dispatcher.DeliverDueAsync(ct) > 0)
        {
        }
    }
}

public sealed class ReconciliationJob(ReconciliationService reconciliation, IOptions<JobOptions> options, TimeProvider time, ILogger<ReconciliationJob> logger)
    : IntervalJob(time, logger)
{
    protected override TimeSpan Interval => options.Value.ReconciliationInterval;

    protected override Task RunOnceAsync(CancellationToken ct) => reconciliation.ReconcileAllAsync(ct);
}
