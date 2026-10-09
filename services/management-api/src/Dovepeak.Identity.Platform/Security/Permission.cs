using Dovepeak.Identity.Persistence.Tenancy;

namespace Dovepeak.Identity.Platform.Security;

/// <summary>
/// Everything a caller can do inside an organization. Organization roles grant fixed sets of permissions;
/// API keys carry an explicit subset, chosen when the key is created.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "\"Permission\" is the domain term; this is not a .NET security permission type.")]
public enum Permission
{
    OrganizationRead,
    OrganizationManage,
    MembersRead,
    MembersManage,
    ProjectsRead,
    ProjectsWrite,
    ProjectsDelete,
    ApplicationsRead,
    ApplicationsWrite,
    CredentialsManage,
    UsersManage,
    ApiKeysManage,
    AuditRead,
    WebhooksManage,
}

public static class Permissions
{
    private static readonly Dictionary<Permission, string> Scopes = new()
    {
        [Permission.OrganizationRead] = "organization:read",
        [Permission.OrganizationManage] = "organization:manage",
        [Permission.MembersRead] = "members:read",
        [Permission.MembersManage] = "members:manage",
        [Permission.ProjectsRead] = "projects:read",
        [Permission.ProjectsWrite] = "projects:write",
        [Permission.ProjectsDelete] = "projects:delete",
        [Permission.ApplicationsRead] = "applications:read",
        [Permission.ApplicationsWrite] = "applications:write",
        [Permission.CredentialsManage] = "credentials:manage",
        [Permission.UsersManage] = "users:manage",
        [Permission.ApiKeysManage] = "api-keys:manage",
        [Permission.AuditRead] = "audit:read",
        [Permission.WebhooksManage] = "webhooks:manage",
    };

    private static readonly Dictionary<string, Permission> ByScope = Scopes.ToDictionary(p => p.Value, p => p.Key);

    private static readonly IReadOnlySet<Permission> Viewer = new HashSet<Permission>
    {
        Permission.OrganizationRead, Permission.MembersRead, Permission.ProjectsRead, Permission.ApplicationsRead,
    };

    private static readonly IReadOnlySet<Permission> Developer = new HashSet<Permission>(Viewer)
    {
        Permission.ProjectsWrite, Permission.ApplicationsWrite, Permission.CredentialsManage, Permission.UsersManage,
    };

    private static readonly IReadOnlySet<Permission> Admin = new HashSet<Permission>(Developer)
    {
        Permission.MembersManage, Permission.ProjectsDelete, Permission.ApiKeysManage, Permission.AuditRead,
        Permission.WebhooksManage,
    };

    private static readonly IReadOnlySet<Permission> Owner = new HashSet<Permission>(Admin)
    {
        Permission.OrganizationManage,
    };

    /// <summary>
    /// Permissions an API key may never hold: organization deletion, membership and API key management
    /// stay with human members (a leaked key cannot lock owners out or mint new keys).
    /// </summary>
    public static readonly IReadOnlySet<Permission> HumanOnly = new HashSet<Permission>
    {
        Permission.OrganizationManage, Permission.MembersManage, Permission.ApiKeysManage,
    };

    public static IReadOnlySet<Permission> For(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => Owner,
        OrganizationRole.Admin => Admin,
        OrganizationRole.Developer => Developer,
        _ => Viewer,
    };

    public static string ToScope(this Permission permission) => Scopes[permission];

    public static bool TryParseScope(string scope, out Permission permission) => ByScope.TryGetValue(scope, out permission);

    public static IReadOnlyCollection<string> AllScopes => Scopes.Values;
}
