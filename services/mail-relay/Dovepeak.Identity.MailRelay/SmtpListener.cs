using Dovepeak.Identity.Platform.Telemetry;
using Microsoft.Extensions.Options;
using SmtpServer;

namespace Dovepeak.Identity.MailRelay;

/// <summary>Runs the SMTP listener; components (authenticator, message store) are resolved from the host's container.</summary>
public sealed class SmtpListener(IServiceProvider services, IOptions<RelayOptions> options) : BackgroundService
{
    public bool Listening { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var relay = options.Value;

        // Publish the delivery counters at zero, so the first failure after a restart is visible to rate() and increase().
        PlatformTelemetry.EmailMessages.Add(0, new KeyValuePair<string, object?>("outcome", "sent"));
        PlatformTelemetry.EmailMessages.Add(0, new KeyValuePair<string, object?>("outcome", "failed"));

        var smtpOptions = new SmtpServerOptionsBuilder()
            .ServerName("dovepeak-mail-relay")
            .MaxMessageSize(relay.MaxMessageBytes)
            .Endpoint(endpoint => endpoint
                .Port(relay.Port)

                // Internal network only (never published); realms authenticate before sending.
                .AllowUnsecureAuthentication(true)
                .AuthenticationRequired(true))
            .Build();

        var server = new SmtpServer.SmtpServer(smtpOptions, services);
        var running = server.StartAsync(stoppingToken);
        Listening = true;
        try
        {
            await running.ConfigureAwait(false);
        }
        finally
        {
            Listening = false;
        }
    }
}
