using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Webhooks;

public sealed record WebhookView(Guid Id, string Url, IReadOnlyList<string> EventTypes, bool Active, DateTimeOffset CreatedAt);

/// <summary>Returned once at creation: the signing secret cannot be retrieved again.</summary>
public sealed record CreatedWebhook(WebhookView Webhook, string SigningSecret);

public sealed record WebhookDeliveryView(Guid Id, Guid EventId, string EventType, WebhookDeliveryStatus Status, int Attempts,
    int? LastStatusCode, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt);

/// <summary>Webhook endpoint management (milestone M3.9).</summary>
public sealed class WebhookService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    IDataProtectionProvider dataProtection,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    public const string ProtectorPurpose = "Dovepeak.Webhooks.SigningSecret.v1";

    public async Task<CreatedWebhook> CreateAsync(Guid organizationId, string url, IReadOnlyList<string>? eventTypes, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.WebhooksManage, ct);
        var uri = WebhookUrlPolicy.Validate(url, options.Value.Webhooks.AllowLoopback);

        var limit = options.Value.Quotas.WebhooksPerOrganization;
        if (await db.WebhookEndpoints.CountAsync(ct) >= limit)
        {
            throw PlatformException.Quota("webhooks", limit);
        }

        var types = (eventTypes ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (types.Any(t => t.Length > 64))
        {
            throw PlatformException.Invalid("eventTypes", "Event types are at most 64 characters.");
        }

        var secret = WebhookSigner.NewSecret();
        var endpoint = new WebhookEndpoint
        {
            OrganizationId = organizationId,
            Url = uri.ToString(),
            ProtectedSecret = dataProtection.CreateProtector(ProtectorPurpose).Protect(secret),
            EventTypes = types,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.WebhookEndpoints.Add(endpoint);
        audit.Record(organizationId, "webhook.created", "webhook", endpoint.Id, new { endpoint.Url, eventTypes = types });
        await db.SaveChangesAsync(ct);

        return new CreatedWebhook(ToView(endpoint), secret);
    }

    public async Task<IReadOnlyList<WebhookView>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.WebhooksManage, ct);
        var endpoints = await db.WebhookEndpoints.OrderBy(w => w.CreatedAt).ToListAsync(ct);
        return endpoints.Select(ToView).ToList();
    }

    public async Task DeleteAsync(Guid organizationId, Guid webhookId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.WebhooksManage, ct);
        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(w => w.Id == webhookId, ct) ?? throw PlatformException.NotFound("webhook");
        db.WebhookEndpoints.Remove(endpoint);
        audit.Record(organizationId, "webhook.deleted", "webhook", webhookId, new { endpoint.Url });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryView>> ListDeliveriesAsync(Guid organizationId, Guid webhookId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.WebhooksManage, ct);
        if (!await db.WebhookEndpoints.AnyAsync(w => w.Id == webhookId, ct))
        {
            throw PlatformException.NotFound("webhook");
        }

        return await db.WebhookDeliveries.Where(d => d.EndpointId == webhookId).OrderByDescending(d => d.CreatedAt).Take(100)
            .Select(d => new WebhookDeliveryView(d.Id, d.EventId, d.EventType, d.Status, d.Attempts, d.LastStatusCode, d.CreatedAt, d.DeliveredAt))
            .ToListAsync(ct);
    }

    private static WebhookView ToView(WebhookEndpoint w) => new(w.Id, w.Url, w.EventTypes, w.Active, w.CreatedAt);
}
