using System.Buffers;
using Dovepeak.Identity.Platform.Telemetry;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Dovepeak.Identity.MailRelay;

/// <summary>Sends a message to the real email provider.</summary>
public interface IUpstreamSender
{
    Task SendAsync(MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken);
}

/// <summary>
/// Applies sending quotas, then forwards the message. Recipients over quota are dropped silently and the client is told
/// the message was accepted: rejecting it would reveal to an attacker that the account exists and is being throttled.
/// An upstream failure is reported as a transient error (451) so Keycloak shows its usual "could not send email" page.
/// </summary>
public sealed partial class RelayMessageStore(IEmailQuota quota, IUpstreamSender upstream, ILogger<RelayMessageStore> logger) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!context.Properties.TryGetValue(RealmAuthenticator.RealmProperty, out var value) || value is not string realm)
        {
            return SmtpResponse.AuthenticationRequired;
        }

        using var activity = PlatformTelemetry.ActivitySource.StartActivity("relay email");
        activity?.SetTag("dovepeak.realm", realm);

        var allowed = new List<MailboxAddress>();
        foreach (var recipient in transaction.To)
        {
            var address = recipient.AsAddress();
            var decision = await quota.ConsumeAsync(realm, address, cancellationToken).ConfigureAwait(false);
            if (decision == QuotaDecision.Allowed)
            {
                allowed.Add(MailboxAddress.Parse(address));
                continue;
            }

            PlatformTelemetry.EmailMessages.Add(1, new("outcome", "throttled"), new("reason", decision.ToString()), new("realm", realm));
            LogThrottled(logger, realm, decision, ValkeyEmailQuota.RecipientKey(realm, address));
        }

        if (allowed.Count == 0)
        {
            return SmtpResponse.Ok;
        }

        try
        {
            var stream = new MemoryStream(buffer.ToArray());
            await using (stream.ConfigureAwait(false))
            {
                var message = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
                var sender = MailboxAddress.Parse(transaction.From.AsAddress());
                await upstream.SendAsync(message, sender, allowed, cancellationToken).ConfigureAwait(false);
            }

            PlatformTelemetry.EmailMessages.Add(allowed.Count, new KeyValuePair<string, object?>("outcome", "sent"));
            return SmtpResponse.Ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
            PlatformTelemetry.EmailMessages.Add(allowed.Count, new KeyValuePair<string, object?>("outcome", "failed"));
            foreach (var recipient in allowed)
            {
                await quota.ReleaseAsync(realm, recipient.Address, CancellationToken.None).ConfigureAwait(false);
            }

            LogUpstreamFailed(logger, ex, realm);
            return new SmtpResponse(TransientFailure, "4.4.1 Upstream temporarily unavailable, try again later");
        }
    }

    // 451: "local error in processing", a transient failure the sender may retry.
    private const SmtpReplyCode TransientFailure = (SmtpReplyCode)451;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email dropped by quota {Decision} in realm {Realm} (account {Account})")]
    private static partial void LogThrottled(ILogger logger, string realm, QuotaDecision decision, string account);

    [LoggerMessage(Level = LogLevel.Error, Message = "Forwarding email for realm {Realm} to the provider failed")]
    private static partial void LogUpstreamFailed(ILogger logger, Exception exception, string realm);
}

/// <summary>Forwards through MailKit, one connection per message (identity email volume is low).</summary>
public sealed class MailKitUpstreamSender(IOptions<UpstreamOptions> options) : IUpstreamSender
{
    public async Task SendAsync(MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
    {
        var upstream = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(upstream.Timeout);

        using var client = new SmtpClient { Timeout = (int)upstream.Timeout.TotalMilliseconds };
        await client.ConnectAsync(upstream.Host, upstream.Port,
            upstream.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, timeout.Token).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(upstream.Username))
        {
            await client.AuthenticateAsync(upstream.Username, upstream.Password ?? string.Empty, timeout.Token).ConfigureAwait(false);
        }

        await client.SendAsync(message, sender, recipients, timeout.Token).ConfigureAwait(false);
        await client.DisconnectAsync(true, timeout.Token).ConfigureAwait(false);
    }
}
