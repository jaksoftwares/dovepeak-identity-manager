using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Applications;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Platform.Projects;

/// <summary>
/// A project's branding for hosted sign-in pages and emails, in every environment. Null fields use the Dovepeak
/// platform defaults. Subjects and introductions are plain text; links and buttons are always added by the platform.
/// </summary>
public sealed record ProjectBranding(
    string? LogoUrl,
    string? PrimaryColor,
    string? EmailVerificationSubject,
    string? EmailVerificationIntro,
    string? PasswordResetSubject,
    string? PasswordResetIntro);

/// <summary>Tenant branding and email templates (milestone M4.3, ADR-0010).</summary>
public sealed class BrandingService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    KeycloakAdminClient engine,
    TimeProvider timeProvider)
{
    public async Task<ProjectBranding> GetAsync(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsRead, ct);
        return ToView(await FindAsync(projectId, ct));
    }

    /// <summary>Replaces the project's branding and applies it to every ready environment's realm.</summary>
    public async Task<ProjectBranding> UpdateAsync(Guid organizationId, Guid projectId, ProjectBranding branding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(branding);
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsWrite, ct);
        var project = await FindAsync(projectId, ct);

        var desired = new TenantBranding(
            Clean(branding.LogoUrl), Clean(branding.PrimaryColor)?.ToLowerInvariant(), Clean(branding.EmailVerificationSubject),
            Clean(branding.EmailVerificationIntro), Clean(branding.PasswordResetSubject), Clean(branding.PasswordResetIntro));
        try
        {
            desired.Validate();
        }
        catch (ArgumentException ex)
        {
            throw PlatformException.Invalid("branding", ex.Message);
        }

        // Apply to the engine first: if it fails, the stored branding is unchanged and reconciliation converges any
        // realm that was already updated back to it.
        var environments = await db.Environments.Where(e => e.ProjectId == projectId && e.State == ProvisioningState.Ready).ToListAsync(ct);
        foreach (var environment in environments)
        {
            await ApplicationService.CallEngineAsync(() => engine.ApplyBrandingAsync(RealmName.Parse(environment.RealmName), desired, ct));
        }

        project.BrandLogoUrl = desired.LogoUrl;
        project.BrandPrimaryColor = desired.PrimaryColor;
        project.EmailVerificationSubject = desired.EmailVerificationSubject;
        project.EmailVerificationIntro = desired.EmailVerificationIntro;
        project.PasswordResetSubject = desired.PasswordResetSubject;
        project.PasswordResetIntro = desired.PasswordResetIntro;
        audit.Record(organizationId, "project.branding_updated", "project", projectId, new
        {
            logoUrl = desired.LogoUrl,
            primaryColor = desired.PrimaryColor,
            customEmailTemplates = new[] { desired.EmailVerificationSubject, desired.EmailVerificationIntro, desired.PasswordResetSubject, desired.PasswordResetIntro }.Count(v => v is not null),
            at = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return ToView(project);
    }

    /// <summary>The engine-level branding for a project (used by provisioning and reconciliation).</summary>
    public static TenantBranding ForProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new TenantBranding(project.BrandLogoUrl, project.BrandPrimaryColor, project.EmailVerificationSubject,
            project.EmailVerificationIntro, project.PasswordResetSubject, project.PasswordResetIntro);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Project> FindAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw PlatformException.NotFound("project");

    private static ProjectBranding ToView(Project p) => new(
        p.BrandLogoUrl, p.BrandPrimaryColor, p.EmailVerificationSubject, p.EmailVerificationIntro, p.PasswordResetSubject, p.PasswordResetIntro);
}
