using System.Text.Json;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Outbox;
using Dovepeak.Identity.Platform.Security;

namespace Dovepeak.Identity.Platform.Audit;

/// <summary>A platform event delivered to webhook subscribers.</summary>
public sealed record PlatformEvent(Guid Id, string Type, DateTimeOffset OccurredAt, Guid OrganizationId, PlatformEventResource Resource,
    PlatformEventActor Actor, JsonElement Data);

public sealed record PlatformEventResource(string Type, string Id);

public sealed record PlatformEventActor(string Type, string Id);

/// <summary>
/// Records administrative changes in the same transaction as the change itself, so a change and its audit record
/// are committed (or rolled back) together. Each recorded action is also published as a webhook event through
/// the outbox. Details must never contain secrets.
/// </summary>
public sealed class ManagementAuditLog(PlatformDbContext db, ICallerAccessor callerAccessor, OutboxWriter outbox, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(Guid organizationId, string action, string resourceType, object resourceId, object? details = null)
    {
        var caller = callerAccessor.Caller;
        var now = timeProvider.GetUtcNow();
        var detailsJson = details is null ? "{}" : JsonSerializer.Serialize(details, JsonOptions);
        var auditEvent = new ManagementAuditEvent
        {
            OrganizationId = organizationId,
            ActorType = caller.ActorType,
            ActorId = caller.Id,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId.ToString() ?? string.Empty,
            Details = detailsJson,
            IpAddress = caller.IpAddress,
            OccurredAt = now,
        };
        db.ManagementAuditEvents.Add(auditEvent);

        using var data = JsonDocument.Parse(detailsJson);
        outbox.Enqueue(organizationId, OutboxMessageTypes.WebhookEvent, new PlatformEvent(
            auditEvent.Id, action, now, organizationId,
            new PlatformEventResource(resourceType, auditEvent.ResourceId),
            new PlatformEventActor(caller.ActorType, caller.Id),
            data.RootElement.Clone()));
    }
}
