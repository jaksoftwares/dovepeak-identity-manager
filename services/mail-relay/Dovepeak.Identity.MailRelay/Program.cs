using System.Text.Json;
using Dovepeak.Identity.MailRelay;
using Dovepeak.Identity.Platform.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmtpServer.Authentication;
using SmtpServer.Storage;
using StackExchange.Redis;

// Dovepeak mail relay: the identity engine sends all email here; the relay applies per-recipient and per-tenant quotas
// (known limitation L-04) and forwards to the real provider, measuring delivery for the email SLO (milestone M6.1).
var builder = WebApplication.CreateSlimBuilder(args);

builder.AddPlatformTelemetry("dovepeak-mail-relay");
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

builder.Services.AddOptions<RelayOptions>().Bind(builder.Configuration.GetSection(RelayOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<UpstreamOptions>().Bind(builder.Configuration.GetSection(UpstreamOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<QuotaOptions>().Bind(builder.Configuration.GetSection(QuotaOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

var redis = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configuration = ConfigurationOptions.Parse(redis);
    configuration.AbortOnConnectFail = false;
    configuration.ConnectTimeout = 3000;
    configuration.SyncTimeout = 2000;
    configuration.AsyncTimeout = 2000;
    return ConnectionMultiplexer.Connect(configuration);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEmailQuota, ValkeyEmailQuota>();
builder.Services.AddSingleton<IUpstreamSender, MailKitUpstreamSender>();
builder.Services.AddSingleton<IUserAuthenticator, RealmAuthenticator>();
builder.Services.AddSingleton<IMessageStore, RelayMessageStore>();
builder.Services.AddSingleton<SmtpListener>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SmtpListener>());

builder.Services.AddHealthChecks()
    .AddCheck("smtp", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<ListenerHealthCheck>("listener", tags: ["ready"]);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains("live"), ResponseWriter = WriteResponse });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteResponse });

await app.RunAsync();

static Task WriteResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
    }));
}

/// <summary>Ready once the SMTP listener is accepting connections.</summary>
internal sealed class ListenerHealthCheck(SmtpListener listener) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(listener.Listening ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}
