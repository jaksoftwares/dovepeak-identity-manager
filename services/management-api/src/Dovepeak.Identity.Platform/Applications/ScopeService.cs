using System.Text.RegularExpressions;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Applications;

public sealed record ScopeView(string Name, string? Description, DateTimeOffset CreatedAt);

/// <summary>
/// OAuth scopes defined per project environment (milestone M3.8). Applications list the scopes they may request;
/// issued access tokens carry the granted ones in the standard "scope" claim, which resource servers enforce.
/// </summary>
public sealed partial class ScopeService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    KeycloakAdminClient engine,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ScopeView> CreateAsync(Guid orgId, Guid projectId, Guid environmentId, string name, string? description, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsWrite, ct);
        var environment = await ReadyEnvironmentAsync(projectId, environmentId, ct);
        ValidateName(name);
        if (description is { Length: > 500 })
        {
            throw PlatformException.Invalid("description", "Descriptions are at most 500 characters.");
        }

        if (await db.EnvironmentScopes.AnyAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct))
        {
            throw PlatformException.Conflict("scope_exists", "This scope already exists in the environment.");
        }

        var limit = options.Value.Quotas.ScopesPerEnvironment;
        if (await db.EnvironmentScopes.CountAsync(s => s.EnvironmentId == environmentId, ct) >= limit)
        {
            throw PlatformException.Quota("scopes in this environment", limit);
        }

        var realm = RealmName.Parse(environment.RealmName);
        var engineId = await ApplicationService.CallEngineAsync(() => engine.CreateClientScopeAsync(realm, name, description, ct));
        var scope = new EnvironmentScope
        {
            OrganizationId = orgId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Name = name,
            Description = description,
            EngineScopeId = engineId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.EnvironmentScopes.Add(scope);
        audit.Record(orgId, "scope.created", "environment", environmentId, new { scope = name });

        try
        {
            await db.SaveOrConflictAsync(ct, "scope_exists", "This scope already exists in the environment.");
        }
        catch
        {
            // Compensate: never leave an engine scope without a platform record.
            await engine.DeleteClientScopeAsync(realm, engineId, CancellationToken.None);
            throw;
        }

        return ToView(scope);
    }

    public async Task<IReadOnlyList<ScopeView>> ListAsync(Guid orgId, Guid projectId, Guid environmentId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsRead, ct);
        await EnvironmentAsync(projectId, environmentId, ct);
        return await db.EnvironmentScopes.Where(s => s.EnvironmentId == environmentId).OrderBy(s => s.Name)
            .Select(s => new ScopeView(s.Name, s.Description, s.CreatedAt)).ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid orgId, Guid projectId, Guid environmentId, string name, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(orgId, Permission.ApplicationsWrite, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var scope = await db.EnvironmentScopes.SingleOrDefaultAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct)
            ?? throw PlatformException.NotFound("scope");

        var users = await db.Applications.Where(a => a.EnvironmentId == environmentId && a.Scopes.Contains(name)).Select(a => a.Name).ToListAsync(ct);
        if (users.Count > 0)
        {
            throw PlatformException.Conflict("scope_in_use", $"Remove the scope from these applications first: {string.Join(", ", users)}.");
        }

        if (scope.EngineScopeId is not null)
        {
            await ApplicationService.CallEngineAsync(() => engine.DeleteClientScopeAsync(RealmName.Parse(environment.RealmName), scope.EngineScopeId, ct));
        }

        db.EnvironmentScopes.Remove(scope);
        audit.Record(orgId, "scope.deleted", "environment", environmentId, new { scope = name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Makes the client's Dovepeak-managed scopes exactly <paramref name="wanted"/>, all as optional scopes. Scopes
    /// Keycloak manages itself (profile, email, …) are left alone. Returns the changes made.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> SyncClientScopesAsync(
        KeycloakAdminClient engine, RealmName realm, string engineClientId, IReadOnlyCollection<string> wanted,
        IReadOnlyCollection<EnvironmentScope> environmentScopes, CancellationToken ct)
    {
        var managed = environmentScopes.Where(s => s.EngineScopeId is not null).ToDictionary(s => s.Name, s => s.EngineScopeId!, StringComparer.Ordinal);
        var changes = new List<string>();

        // A managed scope must never be a default scope: it would appear in tokens without being requested.
        var defaults = await engine.GetAssignedClientScopesAsync(realm, engineClientId, ClientScopeAssignment.Default, ct);
        foreach (var (name, id) in defaults.Where(d => managed.ContainsKey(d.Key)))
        {
            await engine.SetClientScopeAssignmentAsync(realm, engineClientId, id, ClientScopeAssignment.Default, assigned: false, ct);
            changes.Add($"default:-{name}");
        }

        var optional = await engine.GetAssignedClientScopesAsync(realm, engineClientId, ClientScopeAssignment.Optional, ct);
        foreach (var name in wanted.Where(n => !optional.ContainsKey(n)))
        {
            await engine.SetClientScopeAssignmentAsync(realm, engineClientId, managed[name], ClientScopeAssignment.Optional, assigned: true, ct);
            changes.Add($"+{name}");
        }

        foreach (var (name, id) in optional.Where(o => managed.ContainsKey(o.Key) && !wanted.Contains(o.Key)))
        {
            await engine.SetClientScopeAssignmentAsync(realm, engineClientId, id, ClientScopeAssignment.Optional, assigned: false, ct);
            changes.Add($"-{name}");
        }

        return changes;
    }

    private static void ValidateName(string? name)
    {
        if (name is null || !ScopeNamePattern().IsMatch(name))
        {
            throw PlatformException.Invalid("name",
                "Scope names are 1–64 characters: lowercase letters, digits, '.', ':', '/', '_' and '-', starting with a letter or digit.");
        }

        if (ReservedScopeNames.IsReserved(name))
        {
            throw PlatformException.Invalid("name", $"'{name}' is a standard OpenID Connect or identity engine scope and cannot be redefined.");
        }
    }

    private async Task<ProjectEnvironment> EnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken ct) =>
        await db.Environments.SingleOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct)
        ?? throw PlatformException.NotFound("environment");

    private async Task<ProjectEnvironment> ReadyEnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        return environment.State == ProvisioningState.Ready
            ? environment
            : throw PlatformException.Conflict("environment_not_ready", "The environment is not ready yet.");
    }

    private static ScopeView ToView(EnvironmentScope s) => new(s.Name, s.Description, s.CreatedAt);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._:/\-]{0,63}$")]
    private static partial Regex ScopeNamePattern();
}
