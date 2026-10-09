using System.Net;
using System.Net.Sockets;
using System.Text;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Webhooks;

/// <summary>Delivers pending webhooks with signatures, retries and SSRF protection (milestone M3.9).</summary>
public sealed partial class WebhookDispatcher(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<WebhookDispatcher> logger)
{
    public const string HttpClientName = "webhooks";
    public const int MaxAttempts = 8;
    private const int BatchSize = 20;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> DeliverDueAsync(CancellationToken ct)
    {
        var delivered = 0;
        foreach (var id in await ClaimAsync(ct))
        {
            await DeliverAsync(id, ct);
            delivered++;
        }

        return delivered;
    }

    private async Task<IReadOnlyList<Guid>> ClaimAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterSystem();
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + Lease;
        const string pending = nameof(WebhookDeliveryStatus.Pending);

        return await db.Database.SqlQuery<Guid>($"""
            UPDATE webhook_deliveries SET next_attempt_at = {leaseUntil}
            WHERE id IN (
                SELECT id FROM webhook_deliveries
                WHERE status = {pending} AND next_attempt_at <= {now}
                ORDER BY next_attempt_at LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }

    private async Task DeliverAsync(Guid deliveryId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterSystem();

        var delivery = await db.WebhookDeliveries.SingleAsync(d => d.Id == deliveryId, ct);
        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(e => e.Id == delivery.EndpointId, ct);
        var now = timeProvider.GetUtcNow();

        if (endpoint is null || !endpoint.Active)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.LastError = "Endpoint removed or disabled.";
            await db.SaveChangesAsync(ct);
            return;
        }

        delivery.Attempts++;
        try
        {
            var secret = dataProtection.CreateProtector(WebhookService.ProtectorPurpose).Unprotect(endpoint.ProtectedSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(secret, now.ToUnixTimeSeconds(), delivery.Payload));
            request.Headers.Add(WebhookSigner.EventIdHeader, delivery.EventId.ToString());
            request.Headers.Add(WebhookSigner.EventTypeHeader, delivery.EventType);

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            delivery.LastStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Delivered;
                delivery.DeliveredAt = now;
                delivery.LastError = null;
            }
            else
            {
                Reschedule(delivery, $"Endpoint returned HTTP {(int)response.StatusCode}.", now);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Security.Cryptography.CryptographicException)
        {
            Reschedule(delivery, ex is HttpRequestException { InnerException: BlockedDestinationException }
                ? "Destination address is not allowed." : "Delivery failed: " + ex.GetType().Name, now);
            LogFailed(logger, delivery.Id, delivery.Attempts);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void Reschedule(WebhookDelivery delivery, string error, DateTimeOffset now)
    {
        delivery.LastError = error;
        if (delivery.Attempts >= MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            return;
        }

        // 30s, 1m, 2m, 4m ... capped at 6 hours.
        delivery.NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(21600, 30 * Math.Pow(2, delivery.Attempts - 1)));
    }

    /// <summary>
    /// HTTP handler for webhook delivery: short timeouts, no redirects (a redirect could point inside the network),
    /// and an IP check on every connection after DNS resolution.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(IOptions<PlatformOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var allowLoopback = options.Value.Webhooks.AllowLoopback;

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var allowed = addresses.FirstOrDefault(a => WebhookUrlPolicy.IsAllowed(a, allowLoopback))
                    ?? throw new BlockedDestinationException();

                if (addresses.Any(a => !WebhookUrlPolicy.IsAllowed(a, allowLoopback)))
                {
                    throw new BlockedDestinationException();
                }

                var socket = new Socket(allowed.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(allowed, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook delivery {DeliveryId} failed (attempt {Attempt})")]
    private static partial void LogFailed(ILogger logger, Guid deliveryId, int attempt);
}

public sealed class BlockedDestinationException : Exception
{
    public BlockedDestinationException()
        : base("The webhook destination resolves to an address that is not allowed.")
    {
    }

    public BlockedDestinationException(string message)
        : base(message)
    {
    }

    public BlockedDestinationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
