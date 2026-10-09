namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// Role names Keycloak treats as its own administrative roles. Keycloak refuses to grant a role with one of these names
/// (directly or as a composite) unless the granting admin itself holds that exact role, even when the role belongs to an
/// ordinary application client. The least-privilege management account cannot hold them, so application roles may not
/// use them.
/// </summary>
public static class ReservedRoleNames
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "admin",
        "realm-admin",
        "create-realm",
        "create-client",
        "impersonation",
        "view-realm",
        "view-users",
        "view-clients",
        "view-events",
        "view-identity-providers",
        "view-authorization",
        "manage-realm",
        "manage-users",
        "manage-clients",
        "manage-events",
        "manage-identity-providers",
        "manage-authorization",
        "query-users",
        "query-clients",
        "query-realms",
        "query-groups",
    };

    public static bool IsReserved(string name) => Names.Contains(name);
}
