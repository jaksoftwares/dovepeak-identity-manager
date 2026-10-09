using Dovepeak.Identity.Platform;
using Dovepeak.Identity.Workers;
using Dovepeak.Identity.Workers.Audit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPlatform(builder.Configuration);

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

var host = builder.Build();
await host.RunAsync();
