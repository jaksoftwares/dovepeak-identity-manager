using Dovepeak.Identity.ManagementApi.Infrastructure;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Organizations;
using Dovepeak.Identity.Platform.Security;

namespace Dovepeak.Identity.ManagementApi.Endpoints;

/// <summary>Marks every route under /v1/organizations/{orgId}; the tenant-isolation suite enumerates them.</summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class OrganizationScopedAttribute : Attribute;

internal static class OrganizationEndpoints
{
    public sealed record CreateOrganizationRequest(string Slug, string Name);

    public sealed record RenameRequest(string Name);

    public sealed record ChangeRoleRequest(OrganizationRole Role);

    public sealed record InviteRequest(string Email, OrganizationRole Role);

    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/me", (ICallerAccessor accessor) =>
        {
            var caller = accessor.Caller;
            return Results.Ok(new { id = caller.Id, kind = caller.ActorType, caller.Email, caller.EmailVerified, organizationId = caller.ApiKeyOrganizationId });
        }).WithTags("Me");

        v1.MapGet("/me/invitations", (OrganizationService s, CancellationToken ct) => s.ListMyInvitationsAsync(ct)).WithTags("Me");
        v1.MapPost("/me/invitations/{invitationId:guid}/accept",
            async (Guid invitationId, OrganizationService s, CancellationToken ct) => Results.Ok(await s.AcceptInvitationAsync(invitationId, ct)))
            .WithTags("Me");

        v1.MapGet("/organizations", (OrganizationService s, CancellationToken ct) => s.ListMineAsync(ct)).WithTags("Organizations");
        v1.MapPost("/organizations", async (CreateOrganizationRequest request, OrganizationService s, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(request.Slug, request.Name, ct);
                return Results.Created($"/v1/organizations/{created.Id}", created);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithTags("Organizations");

        var org = OrganizationGroup(v1);
        org.MapGet("", (Guid orgId, OrganizationService s, CancellationToken ct) => s.GetAsync(orgId, ct)).WithTags("Organizations");
        org.MapPatch("", (Guid orgId, RenameRequest request, OrganizationService s, CancellationToken ct) => s.RenameAsync(orgId, request.Name, ct))
            .WithTags("Organizations");
        org.MapDelete("", async (Guid orgId, OrganizationService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(orgId, ct);
            return Results.NoContent();
        }).WithTags("Organizations");

        org.MapGet("/members", (Guid orgId, OrganizationService s, CancellationToken ct) => s.ListMembersAsync(orgId, ct)).WithTags("Members");
        org.MapPatch("/members/{userId}", (Guid orgId, string userId, ChangeRoleRequest request, OrganizationService s, CancellationToken ct) =>
            s.ChangeRoleAsync(orgId, userId, request.Role, ct)).WithTags("Members");
        org.MapDelete("/members/{userId}", async (Guid orgId, string userId, OrganizationService s, CancellationToken ct) =>
        {
            await s.RemoveMemberAsync(orgId, userId, ct);
            return Results.NoContent();
        }).WithTags("Members");

        org.MapGet("/invitations", (Guid orgId, OrganizationService s, CancellationToken ct) => s.ListInvitationsAsync(orgId, ct)).WithTags("Members");
        org.MapPost("/invitations", async (Guid orgId, InviteRequest request, OrganizationService s, CancellationToken ct) =>
            {
                var invitation = await s.InviteAsync(orgId, request.Email, request.Role, ct);
                return Results.Created($"/v1/organizations/{orgId}/invitations/{invitation.Id}", invitation);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithTags("Members");
        org.MapDelete("/invitations/{invitationId:guid}", async (Guid orgId, Guid invitationId, OrganizationService s, CancellationToken ct) =>
        {
            await s.RevokeInvitationAsync(orgId, invitationId, ct);
            return Results.NoContent();
        }).WithTags("Members");

        return v1;
    }

    /// <summary>The shared /v1/organizations/{orgId} group. Every service call beneath it authorizes the organization.</summary>
    public static RouteGroupBuilder OrganizationGroup(RouteGroupBuilder v1) =>
        v1.MapGroup("/organizations/{orgId:guid}").WithMetadata(new OrganizationScopedAttribute());
}
