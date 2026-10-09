using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Common;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Platform.Security;

public sealed record OrganizationAccess(Guid OrganizationId, OrganizationRole? Role, IReadOnlySet<Permission> Permissions);

/// <summary>
/// The single authorization gate for organization-scoped operations (milestone M3.2).
/// Callers who are not members, or API keys of another organization, get "not found" — never "forbidden" —
/// so the existence of other tenants' resources is not revealed.
/// On success the unit of work is bound to the organization, activating query filters and row-level security.
/// </summary>
public sealed class TenantAuthorizer(PlatformDbContext db, ICallerAccessor callerAccessor)
{
    public OrganizationAccess? Current { get; private set; }

    public async Task<OrganizationAccess> AuthorizeAsync(Guid organizationId, Permission permission, CancellationToken cancellationToken)
    {
        if (Current is { } existing && existing.OrganizationId == organizationId)
        {
            return Demand(existing, permission);
        }

        var caller = callerAccessor.Caller;
        OrganizationAccess access;

        if (caller.Kind == CallerKind.ApiKey)
        {
            if (caller.ApiKeyOrganizationId != organizationId)
            {
                throw PlatformException.NotFound("organization");
            }

            access = new OrganizationAccess(organizationId, Role: null, caller.ApiKeyPermissions);
        }
        else
        {
            var role = await FindRoleAsync(organizationId, caller.Id, cancellationToken).ConfigureAwait(false)
                ?? throw PlatformException.NotFound("organization");
            access = new OrganizationAccess(organizationId, role, Permissions.For(role));
        }

        db.TenantScope.EnterOrganization(organizationId);
        Current = access;
        return Demand(access, permission);
    }

    private async Task<OrganizationRole?> FindRoleAsync(Guid organizationId, string userId, CancellationToken cancellationToken)
    {
        // Membership resolution is the one tenant-owned read that happens before the tenant is known.
        db.TenantScope.EnterSystem();
        try
        {
            return await db.OrganizationMembers
                .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
                .Select(m => (OrganizationRole?)m.Role)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }

    private static OrganizationAccess Demand(OrganizationAccess access, Permission permission) =>
        access.Permissions.Contains(permission) ? access : throw PlatformException.Forbidden(permission.ToScope());
}
