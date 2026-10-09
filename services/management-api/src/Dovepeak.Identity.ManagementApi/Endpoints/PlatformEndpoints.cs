using Dovepeak.Identity.ManagementApi.Infrastructure;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Credentials;
using Dovepeak.Identity.Platform.Webhooks;

namespace Dovepeak.Identity.ManagementApi.Endpoints;

internal static class PlatformEndpoints
{
    public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

    public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string>? EventTypes);

    public static RouteGroupBuilder MapPlatformEndpoints(this RouteGroupBuilder v1)
    {
        var org = OrganizationEndpoints.OrganizationGroup(v1);

        // Developer API keys
        org.MapGet("/api-keys", (Guid orgId, ApiKeyService s, CancellationToken ct) => s.ListAsync(orgId, ct)).WithTags("API keys");
        org.MapPost("/api-keys", async (Guid orgId, CreateApiKeyRequest request, ApiKeyService s, HttpContext http, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(orgId, request.Name, request.Scopes ?? [], request.ExpiresAt, ct);
                ProjectEndpoints.NoStore(http);
                return Results.Created($"/v1/organizations/{orgId}/api-keys/{created.ApiKey.Id}", created);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithMetadata(new SecretBearingResponseAttribute()).WithTags("API keys");
        org.MapDelete("/api-keys/{keyId:guid}", async (Guid orgId, Guid keyId, ApiKeyService s, CancellationToken ct) =>
        {
            await s.RevokeAsync(orgId, keyId, ct);
            return Results.NoContent();
        }).WithTags("API keys");

        // Webhooks
        org.MapGet("/webhooks", (Guid orgId, WebhookService s, CancellationToken ct) => s.ListAsync(orgId, ct)).WithTags("Webhooks");
        org.MapPost("/webhooks", async (Guid orgId, CreateWebhookRequest request, WebhookService s, HttpContext http, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(orgId, request.Url, request.EventTypes, ct);
                ProjectEndpoints.NoStore(http);
                return Results.Created($"/v1/organizations/{orgId}/webhooks/{created.Webhook.Id}", created);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithMetadata(new SecretBearingResponseAttribute()).WithTags("Webhooks");
        org.MapDelete("/webhooks/{webhookId:guid}", async (Guid orgId, Guid webhookId, WebhookService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(orgId, webhookId, ct);
            return Results.NoContent();
        }).WithTags("Webhooks");
        org.MapGet("/webhooks/{webhookId:guid}/deliveries", (Guid orgId, Guid webhookId, WebhookService s, CancellationToken ct) =>
            s.ListDeliveriesAsync(orgId, webhookId, ct)).WithTags("Webhooks");

        // Audit
        org.MapGet("/audit-events", (Guid orgId, string? source, DateTimeOffset? before, int? limit, AuditQueryService s, CancellationToken ct) =>
            s.QueryAsync(orgId, source, before, limit ?? 50, ct)).WithTags("Audit");

        return v1;
    }
}
