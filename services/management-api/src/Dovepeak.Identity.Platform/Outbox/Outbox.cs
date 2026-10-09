using System.Text.Json;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;

namespace Dovepeak.Identity.Platform.Outbox;

public static class OutboxMessageTypes
{
    public const string ProvisionEnvironment = "environment.provision";
    public const string DeleteRealm = "realm.delete";
    public const string WebhookEvent = "webhook.event";
}

public sealed record ProvisionEnvironmentPayload(Guid EnvironmentId);

public sealed record DeleteRealmPayload(string RealmName, string Cluster);

/// <summary>
/// Adds outbox messages to the current unit of work. They are committed atomically with the business change and
/// processed afterwards by <see cref="OutboxProcessor"/>, with retries, so engine provisioning is never lost.
/// </summary>
public sealed class OutboxWriter(PlatformDbContext db, TimeProvider timeProvider)
{
    public void Enqueue(Guid? organizationId, string type, object payload)
    {
        var now = timeProvider.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            OrganizationId = organizationId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
