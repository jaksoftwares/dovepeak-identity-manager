using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Workers.Audit;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddKeycloakAdmin(builder.Configuration);
builder.Services.AddPlatformPersistence(builder.Configuration);

builder.Services.AddOptions<AuditOptions>()
    .Bind(builder.Configuration.GetSection(AuditOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<AuditEventCollector>();
builder.Services.AddHostedService<AuditCollectionWorker>();

var host = builder.Build();
await host.RunAsync();
