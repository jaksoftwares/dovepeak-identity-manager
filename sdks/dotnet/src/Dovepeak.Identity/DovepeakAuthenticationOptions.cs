using Microsoft.Extensions.Options;

namespace Dovepeak.Identity;

/// <summary>
/// Settings for validating Dovepeak access tokens in an API. Bind them from configuration, for example the
/// <c>Dovepeak</c> section: <c>{ "Issuer": "https://id.example.com/realms/dp-…", "Audience": "orders-api" }</c>.
/// </summary>
public sealed class DovepeakAuthenticationOptions
{
    /// <summary>The environment's issuer, from the application's Integration tab.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>This API's identifier. Tokens must list it in <c>aud</c> (add it as an audience on the calling application).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Require HTTPS for the issuer's metadata. Only disable for a local development stack.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Allowed clock skew in seconds. Defaults to 30.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Also ask the identity provider whether each token is still active (RFC 7662), so revoked sessions are rejected
    /// immediately instead of when the token expires. Costs one request per call (cached briefly).
    /// </summary>
    public DovepeakIntrospectionOptions Introspection { get; set; } = new();
}

/// <summary>Token introspection settings: the API's own confidential client credentials.</summary>
public sealed class DovepeakIntrospectionOptions
{
    /// <summary>Turns introspection on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Client ID used to authenticate to the introspection endpoint.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret. Keep it in a secret store, never in source control.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>How long an "active" answer is cached. Defaults to 10 seconds; 0 disables caching.</summary>
    public int CacheSeconds { get; set; } = 10;
}

/// <summary>Fails start-up on unsafe or incomplete settings rather than accepting tokens wrongly.</summary>
internal sealed class DovepeakAuthenticationOptionsValidator : IValidateOptions<DovepeakAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, DovepeakAuthenticationOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer))
        {
            failures.Add("Dovepeak: Issuer must be an absolute URL.");
        }
        else if (options.RequireHttpsMetadata && issuer.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Dovepeak: Issuer must use HTTPS (set RequireHttpsMetadata=false only for a local development stack).");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Dovepeak: Audience is required. Never accept tokens without checking their audience.");
        }

        if (options.ClockSkewSeconds is < 0 or > 300)
        {
            failures.Add("Dovepeak: ClockSkewSeconds must be between 0 and 300.");
        }

        if (options.Introspection.Enabled && (string.IsNullOrWhiteSpace(options.Introspection.ClientId) || string.IsNullOrWhiteSpace(options.Introspection.ClientSecret)))
        {
            failures.Add("Dovepeak: Introspection requires ClientId and ClientSecret.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
