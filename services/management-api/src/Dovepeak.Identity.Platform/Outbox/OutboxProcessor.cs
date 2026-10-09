using System.Text.Json;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dovepeak.Identity.Platform.Outbox;

/// <summary>
/// Processes due outbox messages with bounded retries and exponential backoff (problem statement §12).
/// Safe to run on several workers at once: messages are claimed with <c>FOR UPDATE SKIP LOCKED</c>.
/// Handlers are idempotent, so a message processed twice (crash after the engine call) converges.
/// </summary>
public sealed partial class OutboxProcessor(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<OutboxProcessor> logger)
{
    public const int MaxAttempts = 10;
    private const int BatchSize = 20;

    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var processed = 0;
        foreach (var id in await ClaimDueAsync(ct))
        {
            await ProcessOneAsync(id, ct);
            processed++;
        }

        return processed;
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterSystem();
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + Lease;

        // Atomically lease due messages: concurrent workers skip rows locked here and, once committed, see the
        // pushed-forward next_attempt_at. A worker that crashes mid-message releases it when the lease expires.
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE outbox_messages SET next_attempt_at = {leaseUntil}
            WHERE id IN (
                SELECT id FROM outbox_messages
                WHERE processed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private async Task ProcessOneAsync(Guid messageId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterSystem();

        var message = await db.OutboxMessages.SingleAsync(m => m.Id == messageId, ct);
        if (message.ProcessedAt is not null)
        {
            return;
        }

        try
        {
            await HandleAsync(scope.ServiceProvider, db, message, ct);
            message.ProcessedAt = timeProvider.GetUtcNow();
            message.LastError = null;
        }
        catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException or TaskCanceledException or DbUpdateException)
        {
            message.Attempts++;
            message.LastError = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            message.NextAttemptAt = timeProvider.GetUtcNow() + Backoff(message.Attempts);
            LogRetry(logger, ex, message.Type, message.Id, message.Attempts);

            if (message.Attempts >= MaxAttempts)
            {
                await MarkFailedAsync(db, message, ct);
                message.ProcessedAt = timeProvider.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task HandleAsync(IServiceProvider services, PlatformDbContext db, OutboxMessage message, CancellationToken ct)
    {
        var admin = services.GetRequiredService<KeycloakAdminClient>();

        switch (message.Type)
        {
            case OutboxMessageTypes.ProvisionEnvironment:
                {
                    var payload = Deserialize<ProvisionEnvironmentPayload>(message);
                    var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == payload.EnvironmentId, ct);
                    if (environment is null)
                    {
                        return; // Project deleted before provisioning ran; DeleteRealm handles any leftovers.
                    }

                    var displayName = await DisplayNameAsync(db, environment, ct);
                    var realm = RealmName.Parse(environment.RealmName);
                    await admin.CreateRealmAsync(realm, displayName, ct);

                    // New environments start with the project's current branding.
                    var project = await db.Projects.SingleAsync(p => p.Id == environment.ProjectId, ct);
                    await admin.ApplyBrandingAsync(realm, Projects.BrandingService.ForProject(project), ct);
                    if (environment.State != ProvisioningState.Ready)
                    {
                        environment.State = ProvisioningState.Ready;
                        environment.LastError = null;
                        environment.UpdatedAt = DateTimeOffset.UtcNow;
                        services.GetRequiredService<Audit.ManagementAuditLog>().Record(environment.OrganizationId, "environment.ready", "environment",
                            environment.Id, new { environment.ProjectId, kind = environment.Kind });
                    }

                    break;
                }

            case OutboxMessageTypes.WebhookEvent:
                {
                    await FanOutWebhookAsync(db, message, ct);
                    break;
                }

            case OutboxMessageTypes.DeleteRealm:
                {
                    var payload = Deserialize<DeleteRealmPayload>(message);
                    await admin.DeleteRealmAsync(RealmName.Parse(payload.RealmName), ct);
                    break;
                }

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'.");
        }
    }

    /// <summary>Creates one pending delivery per subscribed, active endpoint of the event's organization.</summary>
    private static async Task FanOutWebhookAsync(PlatformDbContext db, OutboxMessage message, CancellationToken ct)
    {
        var platformEvent = Deserialize<Audit.PlatformEvent>(message);
        var endpoints = await db.WebhookEndpoints
            .Where(w => w.OrganizationId == platformEvent.OrganizationId && w.Active)
            .ToListAsync(ct);

        foreach (var endpoint in endpoints.Where(w => w.EventTypes.Count == 0 || w.EventTypes.Contains(platformEvent.Type)))
        {
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                OrganizationId = platformEvent.OrganizationId,
                EndpointId = endpoint.Id,
                EventId = platformEvent.Id,
                EventType = platformEvent.Type,
                Payload = message.Payload,
                Status = WebhookDeliveryStatus.Pending,
                NextAttemptAt = message.CreatedAt,
                CreatedAt = message.CreatedAt,
            });
        }
    }

    private static async Task<string> DisplayNameAsync(PlatformDbContext db, ProjectEnvironment environment, CancellationToken ct)
    {
        var project = await db.Projects.SingleAsync(p => p.Id == environment.ProjectId, ct);
        var suffix = environment.Kind == EnvironmentKind.Production ? string.Empty : $" ({environment.Kind.ToString().ToLowerInvariant()})";
        return project.Name + suffix;
    }

    private static async Task MarkFailedAsync(PlatformDbContext db, OutboxMessage message, CancellationToken ct)
    {
        if (message.Type == OutboxMessageTypes.ProvisionEnvironment)
        {
            var payload = Deserialize<ProvisionEnvironmentPayload>(message);
            var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == payload.EnvironmentId, ct);
            if (environment is not null)
            {
                environment.State = ProvisioningState.Failed;
                environment.LastError = "Provisioning failed after repeated attempts. The platform team has been alerted.";
            }
        }
    }

    /// <summary>5s, 10s, 20s ... capped at 15 minutes.</summary>
    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(900, 5 * Math.Pow(2, Math.Max(0, attempt - 1))));

    private static T Deserialize<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, OutboxWriter.JsonOptions)
        ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {Type} {Id} failed (attempt {Attempt})")]
    private static partial void LogRetry(ILogger logger, Exception exception, string type, Guid id, int attempt);
}
