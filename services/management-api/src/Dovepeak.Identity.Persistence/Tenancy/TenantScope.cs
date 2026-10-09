namespace Dovepeak.Identity.Persistence.Tenancy;

/// <summary>Implemented by every entity that belongs to exactly one organization.</summary>
public interface ITenantOwned
{
    Guid OrganizationId { get; }
}

/// <summary>
/// The tenant boundary for the current unit of work (one HTTP request or one background job).
/// </summary>
/// <remarks>
/// Enforced twice: by EF Core global query filters on every <see cref="ITenantOwned"/> entity, and by PostgreSQL
/// row-level security using session settings applied on every connection (threat model I-01).
/// Without an explicit scope, tenant-owned queries return nothing.
/// </remarks>
public sealed class TenantScope
{
    public Guid? OrganizationId { get; private set; }

    /// <summary>System scope bypasses tenant filters. Only for platform jobs and membership resolution.</summary>
    public bool IsSystem { get; private set; }

    public void EnterOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        }

        if (OrganizationId is { } current && current != organizationId)
        {
            throw new InvalidOperationException("A unit of work cannot switch between organizations.");
        }

        OrganizationId = organizationId;
    }

    public void EnterSystem() => IsSystem = true;

    public void ExitSystem() => IsSystem = false;
}
