using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace Dovepeak.Identity.Platform.Applications;

public sealed record RoleView(string Name, string? Description, DateTimeOffset CreatedAt);

public sealed record EnvironmentUserView(string Id, string? Email, bool EmailVerified, bool Enabled, DateTimeOffset CreatedAt);

public sealed record SessionView(string Id, string? IpAddress, DateTimeOffset Started, DateTimeOffset LastAccess, IReadOnlyList<string> Clients);

/// <summary>Application roles, role assignment and end-user session management (milestone M3.8).</summary>
public sealed partial class RoleService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    KeycloakAdminClient engine,
    TimeProvider timeProvider)
{
    // ---------------------------------------------------------------- Roles

    public async Task<RoleView> CreateRoleAsync(Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string name, string? description, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsWrite, ct);
        var (environment, application) = await ApplicationAsync(projectId, environmentId, applicationId, ct);
        if (name is null || !RoleNamePattern().IsMatch(name))
        {
            throw PlatformException.Invalid("name", "Role names are 1–64 characters: letters, digits, '.', ':', '_' and '-'.");
        }

        if (ReservedRoleNames.IsReserved(name))
        {
            throw PlatformException.Invalid("name", $"'{name}' is reserved by the identity engine. Choose another name, such as 'administrator'.");
        }

        if (await db.ApplicationRoles.AnyAsync(r => r.ApplicationId == applicationId && r.Name == name, ct))
        {
            throw PlatformException.Conflict("role_exists", "This role already exists.");
        }

        await ApplicationService.CallEngineAsync(() =>
            engine.CreateClientRoleAsync(RealmName.Parse(environment.RealmName), application.EngineClientId!, name, description, ct));

        var role = new ApplicationRole
        {
            OrganizationId = orgId,
            ApplicationId = applicationId,
            Name = name,
            Description = description?.Trim(),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.ApplicationRoles.Add(role);
        audit.Record(orgId, "role.created", "application", applicationId, new { role = name });
        await db.SaveOrConflictAsync(ct, "role_exists", "This role already exists.");
        return new RoleView(role.Name, role.Description, role.CreatedAt);
    }

    public async Task<IReadOnlyList<RoleView>> ListRolesAsync(Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsRead, ct);
        await ApplicationAsync(projectId, environmentId, applicationId, ct);
        return await db.ApplicationRoles.Where(r => r.ApplicationId == applicationId).OrderBy(r => r.Name)
            .Select(r => new RoleView(r.Name, r.Description, r.CreatedAt)).ToListAsync(ct);
    }

    public async Task DeleteRoleAsync(Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string name, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsWrite, ct);
        var (environment, application) = await ApplicationAsync(projectId, environmentId, applicationId, ct);
        var role = await db.ApplicationRoles.SingleOrDefaultAsync(r => r.ApplicationId == applicationId && r.Name == name, ct)
            ?? throw PlatformException.NotFound("role");

        await ApplicationService.CallEngineAsync(() =>
            engine.DeleteClientRoleAsync(RealmName.Parse(environment.RealmName), application.EngineClientId!, name, ct));
        db.ApplicationRoles.Remove(role);
        audit.Record(orgId, "role.deleted", "application", applicationId, new { role = name });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAssignmentAsync(Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string roleName, string userId, bool assigned, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.UsersManage, ct);
        var (environment, application) = await ApplicationAsync(projectId, environmentId, applicationId, ct);
        if (!await db.ApplicationRoles.AnyAsync(r => r.ApplicationId == applicationId && r.Name == roleName, ct))
        {
            throw PlatformException.NotFound("role");
        }

        var realm = RealmName.Parse(environment.RealmName);
        await RequireUserAsync(realm, userId, ct);
        await ApplicationService.CallEngineAsync(() =>
            engine.SetClientRoleAssignmentAsync(realm, userId, application.EngineClientId!, roleName, assigned, ct));

        audit.Record(orgId, assigned ? "role.assigned" : "role.unassigned", "user", userId, new { application = applicationId, role = roleName });
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- Environment users

    public async Task<IReadOnlyList<EnvironmentUserView>> SearchUsersAsync(Guid orgId, Guid projectId, Guid environmentId, string? email, int first, int max, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.UsersManage, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var users = await ApplicationService.CallEngineAsync(() =>
            engine.SearchUsersAsync(RealmName.Parse(environment.RealmName), email?.Trim().ToLowerInvariant(), Math.Max(0, first), Math.Clamp(max, 1, 100), ct));
        return users.Select(ToUserView).ToList();
    }

    public async Task<IReadOnlyList<SessionView>> ListSessionsAsync(Guid orgId, Guid projectId, Guid environmentId, string userId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.UsersManage, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var realm = RealmName.Parse(environment.RealmName);
        await RequireUserAsync(realm, userId, ct);

        var sessions = await ApplicationService.CallEngineAsync(() => engine.GetUserSessionsAsync(realm, userId, ct));
        return sessions.Select(s => new SessionView(
            s["id"]!.GetValue<string>(),
            s["ipAddress"]?.GetValue<string>(),
            DateTimeOffset.FromUnixTimeMilliseconds(s["start"]!.GetValue<long>()),
            DateTimeOffset.FromUnixTimeMilliseconds(s["lastAccess"]!.GetValue<long>()),
            (s["clients"] as JsonObject ?? []).Select(c => c.Value!.GetValue<string>()).ToList())).ToList();
    }

    /// <summary>Revokes one session, or every session of the user when <paramref name="sessionId"/> is null.</summary>
    public async Task RevokeSessionsAsync(Guid orgId, Guid projectId, Guid environmentId, string userId, string? sessionId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.UsersManage, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var realm = RealmName.Parse(environment.RealmName);
        await RequireUserAsync(realm, userId, ct);

        if (sessionId is null)
        {
            await ApplicationService.CallEngineAsync(() => engine.LogoutUserAsync(realm, userId, ct));
        }
        else
        {
            // The session must belong to this user; otherwise it does not exist for this request.
            var sessions = await ApplicationService.CallEngineAsync(() => engine.GetUserSessionsAsync(realm, userId, ct));
            if (!sessions.Any(s => s["id"]!.GetValue<string>() == sessionId))
            {
                throw PlatformException.NotFound("session");
            }

            await ApplicationService.CallEngineAsync(() => engine.DeleteSessionAsync(realm, sessionId, ct));
        }

        audit.Record(orgId, sessionId is null ? "user.sessions_revoked" : "user.session_revoked", "user", userId, new { session = sessionId });
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireUserAsync(RealmName realm, string userId, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _) || await ApplicationService.CallEngineAsync(() => engine.GetUserAsync(realm, userId, ct)) is null)
        {
            throw PlatformException.NotFound("user");
        }
    }

    private async Task<ProjectEnvironment> EnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var environment = await db.Environments.SingleOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct)
            ?? throw PlatformException.NotFound("environment");
        return environment.State == ProvisioningState.Ready
            ? environment
            : throw PlatformException.Conflict("environment_not_ready", "The environment is not ready yet.");
    }

    private async Task<(ProjectEnvironment Environment, Application Application)> ApplicationAsync(
        Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.EnvironmentId == environmentId, ct)
            ?? throw PlatformException.NotFound("application");
        return (environment, application);
    }

    private static EnvironmentUserView ToUserView(JsonObject u) => new(
        u["id"]!.GetValue<string>(),
        u["email"]?.GetValue<string>(),
        u["emailVerified"]?.GetValue<bool>() ?? false,
        u["enabled"]?.GetValue<bool>() ?? false,
        DateTimeOffset.FromUnixTimeMilliseconds(u["createdTimestamp"]?.GetValue<long>() ?? 0));

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,64}$")]
    private static partial Regex RoleNamePattern();
}
