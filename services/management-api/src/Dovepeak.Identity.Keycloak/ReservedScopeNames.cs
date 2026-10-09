namespace Dovepeak.Identity.Keycloak;

public enum ClientScopeAssignment
{
    /// <summary>Included in a token only when the client requests it with the <c>scope</c> parameter.</summary>
    Optional,

    /// <summary>Always included in the client's tokens.</summary>
    Default,
}

/// <summary>
/// Scope names that Keycloak or OpenID Connect already define. Application-defined scopes may not reuse them: they
/// would collide with the realm's built-in client scopes or change the meaning of standard OIDC requests.
/// </summary>
public static class ReservedScopeNames
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "openid", "profile", "email", "address", "phone", "offline_access", "roles", "web-origins", "role_list",
        "microprofile-jwt", "acr", "basic", "organization", "service_account",
    };

    public static bool IsReserved(string name) => Names.Contains(name);
}
