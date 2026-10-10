using Dovepeak.Identity.Platform;
using Dovepeak.Identity.Platform.Telemetry;
using Dovepeak.Identity.Workers;
using Dovepeak.Identity.Workers.Audit;

// A minimal web host: background jobs plus liveness and readiness endpoints for the orchestrator (milestone M6.2).
var builder = WebApplication.CreateSlimBuilder(args);

builder.AddPlatformTelemetry("dovepeak-workers");
builder.Services.AddPlatform(builder.Configuration);
builder.Services.AddWorkerHealthChecks(builder.Configuration);

// Jobs stop at the end of their current unit of work; outbox and webhook leases make any interrupted item safe to retry.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

builder.Services.AddOptions<AuditOptions>()
    .Bind(builder.Configuration.GetSection(AuditOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<JobOptions>()
    .Bind(builder.Configuration.GetSection(JobOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddScoped<AuditEventCollector>();
builder.Services.AddHostedService<AuditCollectionWorker>();
builder.Services.AddHostedService<OutboxJob>();
builder.Services.AddHostedService<WebhookJob>();
builder.Services.AddHostedService<ReconciliationJob>();

var app = builder.Build();
app.MapWorkerHealthChecks();
await app.RunAsync();
