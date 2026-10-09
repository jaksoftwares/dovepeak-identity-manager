using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Outbox;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Projects;

public sealed record EnvironmentView(Guid Id, Guid ProjectId, EnvironmentKind Kind, ProvisioningState State, string Issuer, DateTimeOffset CreatedAt);

public sealed record ProjectView(Guid Id, string Slug, string Name, DateTimeOffset CreatedAt, IReadOnlyList<EnvironmentView> Environments);

/// <summary>Projects and their environments (milestone M3.4).</summary>
public sealed class ProjectService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    OutboxWriter outbox,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ProjectView> CreateAsync(Guid organizationId, string slug, string name, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsWrite, ct);
        slug = Slug.Validate(slug, nameof(slug));
        name = Slug.ValidateName(name, nameof(name));

        var limit = options.Value.Quotas.ProjectsPerOrganization;
        if (await db.Projects.CountAsync(ct) >= limit)
        {
            throw PlatformException.Quota("projects", limit);
        }

        var now = timeProvider.GetUtcNow();
        var project = new Project { OrganizationId = organizationId, Slug = slug, Name = name, CreatedAt = now };
        db.Projects.Add(project);

        // Every project gets isolated development, staging and production directories (ADR-0002).
        var environments = Enum.GetValues<EnvironmentKind>().Select(kind =>
        {
            var id = Guid.CreateVersion7();
            return new ProjectEnvironment
            {
                Id = id,
                OrganizationId = organizationId,
                ProjectId = project.Id,
                Kind = kind,
                RealmName = RealmName.ForEnvironment(id).Value,
                Cluster = options.Value.DefaultCluster,
                State = ProvisioningState.Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        db.Environments.AddRange(environments);
        foreach (var environment in environments)
        {
            outbox.Enqueue(organizationId, OutboxMessageTypes.ProvisionEnvironment, new ProvisionEnvironmentPayload(environment.Id));
        }

        audit.Record(organizationId, "project.created", "project", project.Id, new { slug, name });
        await db.SaveOrConflictAsync(ct, "slug_taken", "A project with this slug already exists in the organization.");

        return ToView(project, environments);
    }

    public async Task<IReadOnlyList<ProjectView>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsRead, ct);
        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync(ct);
        var environments = await db.Environments.ToListAsync(ct);
        return projects.Select(p => ToView(p, environments.Where(e => e.ProjectId == p.Id))).ToList();
    }

    public async Task<ProjectView> GetAsync(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsRead, ct);
        var project = await FindAsync(projectId, ct);
        var environments = await db.Environments.Where(e => e.ProjectId == projectId).ToListAsync(ct);
        return ToView(project, environments);
    }

    public async Task<ProjectView> RenameAsync(Guid organizationId, Guid projectId, string name, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsWrite, ct);
        var project = await FindAsync(projectId, ct);
        project.Name = Slug.ValidateName(name, nameof(name));
        audit.Record(organizationId, "project.renamed", "project", projectId, new { name = project.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(organizationId, projectId, ct);
    }

    /// <summary>Deletes the project and queues deletion of its identity directories (all users in them are removed).</summary>
    public async Task DeleteAsync(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsDelete, ct);
        var project = await FindAsync(projectId, ct);

        if (await db.Applications.AnyAsync(a => a.ProjectId == projectId, ct))
        {
            throw PlatformException.Conflict("project_not_empty", "Delete the project's applications before deleting the project.");
        }

        var environments = await db.Environments.Where(e => e.ProjectId == projectId).ToListAsync(ct);
        foreach (var environment in environments)
        {
            outbox.Enqueue(organizationId, OutboxMessageTypes.DeleteRealm, new DeleteRealmPayload(environment.RealmName, environment.Cluster));
        }

        db.Environments.RemoveRange(environments);
        db.Projects.Remove(project);
        audit.Record(organizationId, "project.deleted", "project", projectId, new { project.Slug });
        await db.SaveChangesAsync(ct);
    }

    public async Task<EnvironmentView> GetEnvironmentAsync(Guid organizationId, Guid projectId, Guid environmentId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ProjectsRead, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct)
            ?? throw PlatformException.NotFound("environment");
        return ToView(environment);
    }

    private async Task<Project> FindAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw PlatformException.NotFound("project");

    private ProjectView ToView(Project project, IEnumerable<ProjectEnvironment> environments) =>
        new(project.Id, project.Slug, project.Name, project.CreatedAt, environments.OrderBy(e => e.Kind).Select(ToView).ToList());

    internal EnvironmentView ToView(ProjectEnvironment e) =>
        new(e.Id, e.ProjectId, e.Kind, e.State, Issuer(options.Value, e.RealmName), e.CreatedAt);

    internal static string Issuer(PlatformOptions options, string realmName) =>
        new Uri(options.PublicIdentityUrl!, $"realms/{realmName}").ToString();
}
