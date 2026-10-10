using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace Dovepeak.Identity;

/// <summary>Role and scope requirements for Dovepeak tokens.</summary>
public static class DovepeakAuthorization
{
    /// <summary>Requires every listed OAuth scope in the token's <c>scope</c> claim.</summary>
    public static AuthorizationPolicyBuilder RequireScope(this AuthorizationPolicyBuilder policy, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.RequireAuthenticatedUser().RequireAssertion(context => HasScopes(context.User, scopes));
    }

    /// <summary>Requires every listed application role in the token's <c>roles</c> claim.</summary>
    public static AuthorizationPolicyBuilder RequireDovepeakRole(this AuthorizationPolicyBuilder policy, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.RequireAuthenticatedUser().RequireAssertion(context => roles.All(role => context.User.HasClaim(DovepeakClaims.Roles, role)));
    }

    /// <summary>Protects an endpoint with the listed scopes: <c>app.MapGet("/orders", …).RequireScope("orders:read");</c></summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, params string[] scopes)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy.RequireScope(scopes));

    /// <summary>Protects an endpoint with the listed application roles.</summary>
    public static TBuilder RequireDovepeakRole<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy.RequireDovepeakRole(roles));

    /// <summary>The OAuth scopes granted to the token.</summary>
    public static IReadOnlySet<string> GetScopes(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindAll(DovepeakClaims.Scope)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>True when the token carries every listed scope.</summary>
    public static bool HasScopes(this ClaimsPrincipal user, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var granted = user.GetScopes();
        return scopes.All(granted.Contains);
    }
}
