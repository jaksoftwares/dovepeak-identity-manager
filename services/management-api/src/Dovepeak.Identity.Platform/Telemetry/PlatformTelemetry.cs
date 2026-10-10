using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Dovepeak.Identity.Platform.Telemetry;

/// <summary>
/// OpenTelemetry tracing and metrics for the platform services (milestone M6.1). Signals are exported over OTLP only
/// when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so tests and the default local stack run without a collector.
/// </summary>
public static class PlatformTelemetry
{
    public const string SourceName = "Dovepeak.Identity";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    /// <summary>Configuration differences between PostgreSQL and the identity engine that reconciliation reverted.</summary>
    public static readonly Counter<long> DriftCorrections =
        Meter.CreateCounter<long>("dovepeak.reconciliation.drift_corrections", "{correction}", "Drift corrected by reconciliation, by kind.");

    /// <summary>Reconciliation passes over one environment, by outcome (succeeded, failed).</summary>
    public static readonly Counter<long> ReconciliationRuns =
        Meter.CreateCounter<long>("dovepeak.reconciliation.runs", "{run}", "Environment reconciliation passes, by outcome.");

    /// <summary>Outbox messages processed, by type and outcome (succeeded, retried, failed).</summary>
    public static readonly Counter<long> OutboxMessages =
        Meter.CreateCounter<long>("dovepeak.outbox.messages", "{message}", "Outbox messages processed, by type and outcome.");

    /// <summary>Webhook delivery attempts, by outcome (delivered, retried, failed).</summary>
    public static readonly Counter<long> WebhookDeliveries =
        Meter.CreateCounter<long>("dovepeak.webhook.deliveries", "{delivery}", "Webhook delivery attempts, by outcome.");

    /// <summary>Identity-engine events copied into the audit store.</summary>
    public static readonly Counter<long> AuditEventsCollected =
        Meter.CreateCounter<long>("dovepeak.audit.events_collected", "{event}", "Identity-engine events collected into the audit store.");

    /// <summary>Messages handled by the mail relay, by outcome (sent, failed, throttled).</summary>
    public static readonly Counter<long> EmailMessages =
        Meter.CreateCounter<long>("dovepeak.email.messages", "{message}", "Emails handled by the mail relay, by outcome.");

    /// <summary>Adds tracing and metrics for a platform service.</summary>
    public static IHostApplicationBuilder AddPlatformTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: "dovepeak-identity",
                    serviceVersion: typeof(PlatformTelemetry).Assembly.GetName().Version?.ToString())
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(SourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Probes and scrapes are not user traffic.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation(options =>
                {
                    // Never record query strings: Keycloak action tokens and codes travel there.
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                        activity.SetTag("url.full", request.RequestUri?.GetLeftPart(UriPartial.Path));
                })
                .AddNpgsql()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql")
                .AddOtlpExporter());

        return builder;
    }
}
