using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Workers.Audit;

/// <summary>
/// Copies authentication and admin events from the identity engine into the Dovepeak audit store
/// (milestone M2.5). Idempotent: events are de-duplicated by their source ID, so re-running is safe.
/// </summary>
public sealed partial class AuditEventCollector(
    KeycloakAdminClient admin,
    PlatformDbContext db,
    TimeProvider timeProvider,
    ILogger<AuditEventCollector> logger)
{
    private const int PageSize = 100;
    private const int MaxPagesPerRun = 50;

    // Events can share a millisecond; re-reading a short window avoids missing any (duplicates are ignored).
    private static readonly TimeSpan CheckpointOverlap = TimeSpan.FromSeconds(5);

    // Only these details are stored. Everything else (authorization codes, token IDs, code IDs) is dropped.
    private static readonly HashSet<string> AllowedDetails = new(StringComparer.Ordinal)
    {
        "auth_method", "auth_type", "grant_type", "response_type", "redirect_uri", "username", "email",
        "reason", "identity_provider", "remember_me", "consent", "custom_required_action", "credential_type",
    };

    public async Task<int> CollectAllAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var name in await admin.ListRealmNamesAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                total += await CollectRealmAsync(RealmName.Parse(name), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is KeycloakAdminException or DbUpdateException or HttpRequestException)
            {
                // One failing tenant must not stop collection for the others.
                LogRealmFailed(logger, ex, name);
            }
        }

        return total;
    }

    public async Task<int> CollectRealmAsync(RealmName realm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(realm);

        var authentication = await CollectAsync(
            realm, AuditSources.Authentication,
            (first, max, ct) => admin.GetEventsAsync(realm, first, max, ct),
            MapAuthenticationEvent, cancellationToken).ConfigureAwait(false);

        var administrative = await CollectAsync(
            realm, AuditSources.Admin,
            (first, max, ct) => admin.GetAdminEventsAsync(realm, first, max, ct),
            MapAdminEvent, cancellationToken).ConfigureAwait(false);

        return authentication + administrative;
    }

    private async Task<int> CollectAsync(
        RealmName realm,
        string source,
        Func<int, int, CancellationToken, Task<IReadOnlyList<JsonObject>>> fetchPage,
        Func<RealmName, JsonObject, DateTimeOffset, AuditEvent> map,
        CancellationToken cancellationToken)
    {
        var checkpoint = await db.AuditCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Realm == realm.Value && c.Source == source, cancellationToken).ConfigureAwait(false);
        var since = (checkpoint?.LastEventAt ?? DateTimeOffset.MinValue) - (checkpoint is null ? TimeSpan.Zero : CheckpointOverlap);
        var now = timeProvider.GetUtcNow();

        var batch = new List<AuditEvent>();
        for (var page = 0; page < MaxPagesPerRun; page++)
        {
            // Pages are newest first; stop at the first event older than the checkpoint.
            var events = await fetchPage(page * PageSize, PageSize, cancellationToken).ConfigureAwait(false);
            var reachedCheckpoint = false;

            foreach (var raw in events)
            {
                var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(raw["time"]!.GetValue<long>());
                if (occurredAt < since)
                {
                    reachedCheckpoint = true;
                    break;
                }

                batch.Add(map(realm, raw, now));
            }

            if (reachedCheckpoint || events.Count < PageSize)
            {
                break;
            }
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        var newest = batch.Max(e => e.OccurredAt);

        // The whole write is one retriable unit: inserts are idempotent and the checkpoint only moves forward.
        var strategy = db.Database.CreateExecutionStrategy();
        var inserted = await strategy.ExecuteAsync(async ct =>
        {
            var count = 0;
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var e in batch)
            {
                count += await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO audit_events (id, realm, source, source_event_id, type, occurred_at, recorded_at,
                                              user_id, client_id, session_id, ip_address, error, details)
                    VALUES ({e.Id}, {e.Realm}, {e.Source}, {e.SourceEventId}, {e.Type}, {e.OccurredAt}, {e.RecordedAt},
                            {e.UserId}, {e.ClientId}, {e.SessionId}, {e.IpAddress}, {e.Error}, {e.Details}::jsonb)
                    ON CONFLICT (realm, source, source_event_id) DO NOTHING
                    """, ct).ConfigureAwait(false);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_checkpoints (realm, source, last_event_at, updated_at)
                VALUES ({realm.Value}, {source}, {newest}, {now})
                ON CONFLICT (realm, source) DO UPDATE
                SET last_event_at = GREATEST(audit_checkpoints.last_event_at, EXCLUDED.last_event_at),
                    updated_at = EXCLUDED.updated_at
                """, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return count;
        }, cancellationToken).ConfigureAwait(false);

        if (inserted > 0)
        {
            LogCollected(logger, inserted, source, realm.Value);
        }

        return inserted;
    }

    private static AuditEvent MapAuthenticationEvent(RealmName realm, JsonObject raw, DateTimeOffset recordedAt) => new()
    {
        Realm = realm.Value,
        Source = AuditSources.Authentication,
        SourceEventId = raw["id"]?.GetValue<string>() ?? FallbackId(raw),
        Type = raw["type"]!.GetValue<string>(),
        OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(raw["time"]!.GetValue<long>()),
        RecordedAt = recordedAt,
        UserId = raw["userId"]?.GetValue<string>(),
        ClientId = raw["clientId"]?.GetValue<string>(),
        SessionId = raw["sessionId"]?.GetValue<string>(),
        IpAddress = raw["ipAddress"]?.GetValue<string>(),
        Error = raw["error"]?.GetValue<string>(),
        Details = FilterDetails(raw["details"] as JsonObject),
    };

    private static AuditEvent MapAdminEvent(RealmName realm, JsonObject raw, DateTimeOffset recordedAt)
    {
        var auth = raw["authDetails"] as JsonObject;
        var details = new JsonObject
        {
            ["resource_type"] = raw["resourceType"]?.GetValue<string>(),
            ["resource_path"] = raw["resourcePath"]?.GetValue<string>(),
        };

        return new AuditEvent
        {
            Realm = realm.Value,
            Source = AuditSources.Admin,
            SourceEventId = raw["id"]?.GetValue<string>() ?? FallbackId(raw),
            Type = $"{raw["operationType"]?.GetValue<string>()}:{raw["resourceType"]?.GetValue<string>()}",
            OccurredAt = DateTimeOffset.FromUnixTimeMilliseconds(raw["time"]!.GetValue<long>()),
            RecordedAt = recordedAt,
            UserId = auth?["userId"]?.GetValue<string>(),
            ClientId = auth?["clientId"]?.GetValue<string>(),
            IpAddress = auth?["ipAddress"]?.GetValue<string>(),
            Error = raw["error"]?.GetValue<string>(),
            Details = details.ToJsonString(),
        };
    }

    internal static string FilterDetails(JsonObject? details)
    {
        var filtered = new JsonObject();
        if (details is not null)
        {
            foreach (var (key, value) in details)
            {
                if (AllowedDetails.Contains(key))
                {
                    filtered[key] = value?.DeepClone();
                }
            }
        }

        return filtered.ToJsonString();
    }

    private static string FallbackId(JsonObject raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToJsonString(JsonSerializerOptions.Default))));

    [LoggerMessage(Level = LogLevel.Information, Message = "Collected {Count} {Source} audit events for realm {Realm}")]
    private static partial void LogCollected(ILogger logger, int count, string source, string realm);

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit collection failed for realm {Realm}")]
    private static partial void LogRealmFailed(ILogger logger, Exception exception, string realm);
}
