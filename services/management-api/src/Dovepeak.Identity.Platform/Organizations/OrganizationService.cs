using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Organizations;

public sealed record OrganizationView(Guid Id, string Slug, string Name, OrganizationRole? Role, DateTimeOffset CreatedAt);

public sealed record MemberView(string UserId, string Email, OrganizationRole Role, DateTimeOffset JoinedAt);

public sealed record InvitationView(Guid Id, Guid OrganizationId, string Email, OrganizationRole Role, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

/// <summary>Organizations, membership and invitations (milestone M3.3).</summary>
public sealed class OrganizationService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ICallerAccessor callerAccessor,
    ManagementAuditLog audit,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    private Caller Caller => callerAccessor.Caller;

    // ---------------------------------------------------------------- Organizations

    public async Task<OrganizationView> CreateAsync(string slug, string name, CancellationToken ct)
    {
        Caller.RequireDeveloper();
        slug = Slug.Validate(slug, nameof(slug));
        name = Slug.ValidateName(name, nameof(name));
        var now = timeProvider.GetUtcNow();

        db.TenantScope.EnterSystem();
        try
        {
            var owned = await db.OrganizationMembers.CountAsync(m => m.UserId == Caller.Id && m.Role == OrganizationRole.Owner, ct);
            var limit = options.Value.Quotas.OrganizationsPerDeveloper;
            if (owned >= limit)
            {
                throw PlatformException.Quota("organizations", limit);
            }

            if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
            {
                throw PlatformException.Conflict("slug_taken", "An organization with this slug already exists.");
            }

            var organization = new Organization { Slug = slug, Name = name, CreatedBy = Caller.Id, CreatedAt = now };
            db.Organizations.Add(organization);
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organization.Id,
                UserId = Caller.Id,
                Email = Caller.Email ?? string.Empty,
                Role = OrganizationRole.Owner,
                JoinedAt = now,
            });
            audit.Record(organization.Id, "organization.created", "organization", organization.Id, new { slug, name });
            await SaveAsync(ct);

            return new OrganizationView(organization.Id, slug, name, OrganizationRole.Owner, now);
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }

    /// <summary>Organizations the caller belongs to (developers) or the key's organization (API keys).</summary>
    public async Task<IReadOnlyList<OrganizationView>> ListMineAsync(CancellationToken ct)
    {
        db.TenantScope.EnterSystem();
        try
        {
            if (Caller.Kind == CallerKind.ApiKey)
            {
                return await db.Organizations.Where(o => o.Id == Caller.ApiKeyOrganizationId)
                    .Select(o => new OrganizationView(o.Id, o.Slug, o.Name, null, o.CreatedAt)).ToListAsync(ct);
            }

            return await (from m in db.OrganizationMembers
                          join o in db.Organizations on m.OrganizationId equals o.Id
                          where m.UserId == Caller.Id
                          orderby o.Name
                          select new OrganizationView(o.Id, o.Slug, o.Name, m.Role, o.CreatedAt)).ToListAsync(ct);
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }

    public async Task<OrganizationView> GetAsync(Guid organizationId, CancellationToken ct)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.OrganizationRead, ct);
        var o = await db.Organizations.SingleAsync(x => x.Id == organizationId, ct);
        return new OrganizationView(o.Id, o.Slug, o.Name, access.Role, o.CreatedAt);
    }

    public async Task<OrganizationView> RenameAsync(Guid organizationId, string name, CancellationToken ct)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.OrganizationManage, ct);
        var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, ct);
        organization.Name = Slug.ValidateName(name, nameof(name));
        audit.Record(organizationId, "organization.renamed", "organization", organizationId, new { name = organization.Name });
        await SaveAsync(ct);
        return new OrganizationView(organization.Id, organization.Slug, organization.Name, access.Role, organization.CreatedAt);
    }

    public async Task DeleteAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.OrganizationManage, ct);
        if (await db.Projects.AnyAsync(ct))
        {
            throw PlatformException.Conflict("organization_not_empty", "Delete all projects before deleting the organization.");
        }

        var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId, ct);
        db.Organizations.Remove(organization);
        await SaveAsync(ct);
    }

    // ---------------------------------------------------------------- Members

    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.MembersRead, ct);
        return await db.OrganizationMembers.OrderBy(m => m.Email)
            .Select(m => new MemberView(m.UserId, m.Email, m.Role, m.JoinedAt)).ToListAsync(ct);
    }

    public async Task<MemberView> ChangeRoleAsync(Guid organizationId, string userId, OrganizationRole role, CancellationToken ct)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.MembersManage, ct);
        var member = await db.OrganizationMembers.SingleOrDefaultAsync(m => m.UserId == userId, ct)
            ?? throw PlatformException.NotFound("member");

        // Only owners can grant or take away ownership.
        if ((role == OrganizationRole.Owner || member.Role == OrganizationRole.Owner) && access.Role != OrganizationRole.Owner)
        {
            throw PlatformException.Forbidden(Permission.OrganizationManage.ToScope());
        }

        if (member.Role == OrganizationRole.Owner && role != OrganizationRole.Owner)
        {
            await EnsureAnotherOwnerAsync(userId, ct);
        }

        var previous = member.Role;
        member.Role = role;
        audit.Record(organizationId, "member.role_changed", "member", userId, new { from = previous, to = role });
        await SaveAsync(ct);
        return new MemberView(member.UserId, member.Email, member.Role, member.JoinedAt);
    }

    public async Task RemoveMemberAsync(Guid organizationId, string userId, CancellationToken ct)
    {
        // Members may always leave; removing someone else requires members:manage.
        var self = Caller.Kind == CallerKind.Developer && Caller.Id == userId;
        var access = await authorizer.AuthorizeAsync(organizationId, self ? Permission.OrganizationRead : Permission.MembersManage, ct);

        var member = await db.OrganizationMembers.SingleOrDefaultAsync(m => m.UserId == userId, ct)
            ?? throw PlatformException.NotFound("member");

        if (member.Role == OrganizationRole.Owner)
        {
            if (!self && access.Role != OrganizationRole.Owner)
            {
                throw PlatformException.Forbidden(Permission.OrganizationManage.ToScope());
            }

            await EnsureAnotherOwnerAsync(userId, ct);
        }

        db.OrganizationMembers.Remove(member);
        audit.Record(organizationId, "member.removed", "member", userId);
        await SaveAsync(ct);
    }

    private async Task EnsureAnotherOwnerAsync(string userId, CancellationToken ct)
    {
        if (!await db.OrganizationMembers.AnyAsync(m => m.Role == OrganizationRole.Owner && m.UserId != userId, ct))
        {
            throw PlatformException.Conflict("last_owner", "An organization must keep at least one owner.");
        }
    }

    // ---------------------------------------------------------------- Invitations

    public async Task<InvitationView> InviteAsync(Guid organizationId, string email, OrganizationRole role, CancellationToken ct)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.MembersManage, ct);
        if (role == OrganizationRole.Owner && access.Role != OrganizationRole.Owner)
        {
            throw PlatformException.Forbidden(Permission.OrganizationManage.ToScope());
        }

        var normalized = NormalizeEmail(email);
        if (await db.OrganizationMembers.AnyAsync(m => m.Email == normalized, ct))
        {
            throw PlatformException.Conflict("already_member", "This person is already a member.");
        }

        var now = timeProvider.GetUtcNow();
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = normalized,
            Role = role,
            CreatedBy = Caller.Id,
            CreatedAt = now,
            ExpiresAt = now + InvitationLifetime,
        };
        db.OrganizationInvitations.Add(invitation);
        audit.Record(organizationId, "invitation.created", "invitation", invitation.Id, new { email = normalized, role });
        await SaveAsync(ct);
        return ToView(invitation);
    }

    public async Task<IReadOnlyList<InvitationView>> ListInvitationsAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.MembersRead, ct);
        var now = timeProvider.GetUtcNow();
        var pending = await db.OrganizationInvitations
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return pending.Select(ToView).ToList();
    }

    public async Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.MembersManage, ct);
        var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.RevokedAt == null, ct)
            ?? throw PlatformException.NotFound("invitation");
        invitation.RevokedAt = timeProvider.GetUtcNow();
        audit.Record(organizationId, "invitation.revoked", "invitation", invitationId);
        await SaveAsync(ct);
    }

    /// <summary>Pending invitations addressed to the signed-in developer's verified email address.</summary>
    public async Task<IReadOnlyList<InvitationView>> ListMyInvitationsAsync(CancellationToken ct)
    {
        var email = VerifiedEmail();
        var now = timeProvider.GetUtcNow();
        db.TenantScope.EnterSystem();
        try
        {
            var pending = await db.OrganizationInvitations
                .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
                .ToListAsync(ct);
            return pending.Select(ToView).ToList();
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }

    public async Task<OrganizationView> AcceptInvitationAsync(Guid invitationId, CancellationToken ct)
    {
        var email = VerifiedEmail();
        var now = timeProvider.GetUtcNow();
        db.TenantScope.EnterSystem();
        try
        {
            // The invitation must be addressed to this developer's verified email; otherwise it does not exist for them.
            var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(i =>
                    i.Id == invitationId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct)
                ?? throw PlatformException.NotFound("invitation");

            if (await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == invitation.OrganizationId && m.UserId == Caller.Id, ct))
            {
                throw PlatformException.Conflict("already_member", "You are already a member of this organization.");
            }

            invitation.AcceptedAt = now;
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = invitation.OrganizationId,
                UserId = Caller.Id,
                Email = email,
                Role = invitation.Role,
                JoinedAt = now,
            });
            audit.Record(invitation.OrganizationId, "invitation.accepted", "invitation", invitation.Id, new { role = invitation.Role });
            await SaveAsync(ct);

            var organization = await db.Organizations.SingleAsync(o => o.Id == invitation.OrganizationId, ct);
            return new OrganizationView(organization.Id, organization.Slug, organization.Name, invitation.Role, organization.CreatedAt);
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }

    private string VerifiedEmail()
    {
        Caller.RequireDeveloper();
        if (!Caller.EmailVerified || string.IsNullOrEmpty(Caller.Email))
        {
            throw new PlatformException(PlatformErrorKind.Forbidden, "email_not_verified", "A verified email address is required.");
        }

        return NormalizeEmail(Caller.Email);
    }

    private static string NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 320 || !System.Net.Mail.MailAddress.TryCreate(trimmed, out _))
        {
            throw PlatformException.Invalid("email", "A valid email address is required.");
        }

        return trimmed;
    }

    private static InvitationView ToView(OrganizationInvitation i) =>
        new(i.Id, i.OrganizationId, i.Email, i.Role, i.ExpiresAt, i.CreatedAt);

    private Task SaveAsync(CancellationToken ct) => db.SaveOrConflictAsync(ct);
}
