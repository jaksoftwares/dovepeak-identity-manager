using System.Globalization;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// Per-client token and session lifetimes, in seconds. A null value inherits the realm baseline
/// (identity/keycloak/realm-template/realm-template.json, ADR-0003).
/// </summary>
/// <remarks>
/// Keycloak caps client session timeouts at the realm's SSO session values, so larger values would be silently
/// ignored. They are rejected instead, so the configured value is always the effective one.
/// </remarks>
public sealed record TokenPolicy(int? AccessTokenLifetimeSeconds = null, int? SessionIdleTimeoutSeconds = null, int? SessionMaxLifetimeSeconds = null)
{
    // Realm baseline. RealmTemplateTests keeps these in sync with the realm template.
    public const int RealmAccessTokenLifetimeSeconds = 600;
    public const int RealmSessionIdleTimeoutSeconds = 1800;
    public const int RealmSessionMaxLifetimeSeconds = 43200;

    /// <summary>ADR-0003: access tokens are configurable per application between 5 and 60 minutes.</summary>
    public const int MinAccessTokenLifetimeSeconds = 300;

    /// <summary>Access tokens cannot be revoked at resource servers (limitation L-01), so they stay short-lived.</summary>
    public const int MaxAccessTokenLifetimeSeconds = 3600;

    public const int MinSessionSeconds = 300;

    internal const string AccessTokenLifespanAttribute = "access.token.lifespan";
    internal const string SessionIdleTimeoutAttribute = "client.session.idle.timeout";
    internal const string SessionMaxLifespanAttribute = "client.session.max.lifespan";

    public static TokenPolicy Inherit { get; } = new();

    public int EffectiveAccessTokenLifetimeSeconds => AccessTokenLifetimeSeconds ?? RealmAccessTokenLifetimeSeconds;

    public int EffectiveSessionIdleTimeoutSeconds => SessionIdleTimeoutSeconds ?? RealmSessionIdleTimeoutSeconds;

    public int EffectiveSessionMaxLifetimeSeconds => SessionMaxLifetimeSeconds ?? RealmSessionMaxLifetimeSeconds;

    public void Validate(ClientKind kind)
    {
        EnsureRange(AccessTokenLifetimeSeconds, nameof(AccessTokenLifetimeSeconds), MinAccessTokenLifetimeSeconds, MaxAccessTokenLifetimeSeconds);

        if (kind == ClientKind.Machine && (SessionIdleTimeoutSeconds is not null || SessionMaxLifetimeSeconds is not null))
        {
            throw new ArgumentException("Machine-to-machine applications have no user sessions; session timeouts do not apply.");
        }

        EnsureRange(SessionIdleTimeoutSeconds, nameof(SessionIdleTimeoutSeconds), MinSessionSeconds, RealmSessionIdleTimeoutSeconds);
        EnsureRange(SessionMaxLifetimeSeconds, nameof(SessionMaxLifetimeSeconds), MinSessionSeconds, RealmSessionMaxLifetimeSeconds);

        if (SessionMaxLifetimeSeconds < SessionIdleTimeoutSeconds)
        {
            throw new ArgumentException("tokenPolicy.sessionMaxLifetimeSeconds must not be shorter than the session idle timeout.");
        }
    }

    /// <summary>Keycloak client attribute values; an empty value makes Keycloak use the realm setting.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToAttributes() =>
    [
        new(AccessTokenLifespanAttribute, Format(AccessTokenLifetimeSeconds)),
        new(SessionIdleTimeoutAttribute, Format(SessionIdleTimeoutSeconds)),
        new(SessionMaxLifespanAttribute, Format(SessionMaxLifetimeSeconds)),
    ];

    private static string Format(int? seconds) => seconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static void EnsureRange(int? value, string name, int min, int max)
    {
        if (value is { } seconds && (seconds < min || seconds > max))
        {
            var field = char.ToLowerInvariant(name[0]) + name[1..];
            throw new ArgumentException($"tokenPolicy.{field} must be between {min} and {max} seconds.");
        }
    }
}
