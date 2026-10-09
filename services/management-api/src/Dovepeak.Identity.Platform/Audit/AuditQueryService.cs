using System.Text.Json;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Platform.Audit;

public sealed record AuditEventView(
    Guid Id,
    string Source,
    string Type,
    DateTimeOffset OccurredAt,
    string? ActorType,
    string? ActorId,
    string? ResourceType,
    string? ResourceId,
    Guid? EnvironmentId,
    string? ClientId,
    string? IpAddress,
    string? Error,
    JsonElement Details);

public sealed record AuditPage(IReadOnlyList<AuditEventView> Events, DateTimeOffset? NextBefore);

/// <summary>
/// The organization's audit trail: administrative changes plus end-user authentication events from the organization's
/// own environments only (milestone M3.9).
/// </summary>
public sealed class AuditQueryService(PlatformDbContext db, TenantAuthorizer authorizer)
{
    public async Task<AuditPage> QueryAsync(Guid organizationId, string? source, DateTimeOffset? before, int limit, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.AuditRead, ct);
        limit = Math.Clamp(limit, 1, 200);
        var cutoff = before ?? DateTimeOffset.MaxValue;
        var results = new List<AuditEventView>();

        if (source is null or "management")
        {
            var management = await db.ManagementAuditEvents
                .Where(e => e.OccurredAt < cutoff).OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(ct);
            results.AddRange(management.Select(e => new AuditEventView(
                e.Id, "management", e.Action, e.OccurredAt, e.ActorType, e.ActorId, e.ResourceType, e.ResourceId,
                null, null, e.IpAddress, null, Parse(e.Details))));
        }

        if (source is null or "authentication" or "admin")
        {
            // Authentication events are keyed by realm; only realms of this organization's environments are visible.
            var realms = await db.Environments.Select(e => new { e.Id, e.RealmName }).ToListAsync(ct);
            var realmToEnvironment = realms.ToDictionary(r => r.RealmName, r => r.Id, StringComparer.Ordinal);
            var realmNames = realmToEnvironment.Keys.ToList();

            var engineEvents = await db.AuditEvents
                .Where(e => realmNames.Contains(e.Realm) && e.OccurredAt < cutoff && (source == null || e.Source == source))
                .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(ct);
            results.AddRange(engineEvents.Select(e => new AuditEventView(
                e.Id, e.Source, e.Type, e.OccurredAt, e.UserId is null ? null : "end_user", e.UserId, null, null,
                realmToEnvironment[e.Realm], e.ClientId, e.IpAddress, e.Error, Parse(e.Details))));
        }

        if (source is not (null or "management" or "authentication" or "admin"))
        {
            throw PlatformException.Invalid("source", "Source must be 'management', 'authentication' or 'admin'.");
        }

        var page = results.OrderByDescending(e => e.OccurredAt).Take(limit).ToList();
        return new AuditPage(page, page.Count == limit ? page[^1].OccurredAt : null);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
