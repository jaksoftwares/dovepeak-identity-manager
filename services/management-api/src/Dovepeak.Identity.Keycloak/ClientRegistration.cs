namespace Dovepeak.Identity.Keycloak;

public enum ClientKind
{
    /// <summary>Single-page or native application. Cannot hold a secret; uses Authorization Code with PKCE.</summary>
    Public,

    /// <summary>Server-side web application or BFF. Holds a secret; uses Authorization Code with PKCE.</summary>
    Confidential,

    /// <summary>Backend service using the client credentials grant. No end-user login.</summary>
    Machine,
}

public sealed record ClientRegistration
{
    public ClientRegistration(string clientId, ClientKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ClientId = clientId;
        Kind = kind;
    }

    public string ClientId { get; }

    public ClientKind Kind { get; }

    public string? Name { get; init; }

    public IReadOnlyList<Uri> RedirectUris { get; init; } = [];

    public IReadOnlyList<Uri> PostLogoutRedirectUris { get; init; } = [];

    /// <summary>Allowed CORS origins, e.g. https://app.example.com. No wildcards.</summary>
    public IReadOnlyList<Uri> WebOrigins { get; init; } = [];

    /// <summary>Resource server audiences added to this client's access tokens.</summary>
    public IReadOnlyList<string> Audiences { get; init; } = [];

    public void Validate()
    {
        if (Kind == ClientKind.Machine && (RedirectUris.Count > 0 || PostLogoutRedirectUris.Count > 0))
        {
            throw new ArgumentException("Machine clients do not use redirect URIs.");
        }

        if (Kind != ClientKind.Machine && RedirectUris.Count == 0)
        {
            throw new ArgumentException("Interactive clients require at least one redirect URI.");
        }

        foreach (var uri in RedirectUris.Concat(PostLogoutRedirectUris))
        {
            RedirectUriPolicy.EnsureAllowed(uri);
        }

        foreach (var origin in WebOrigins)
        {
            RedirectUriPolicy.EnsureAllowedOrigin(origin);
        }
    }
}

/// <summary>
/// Redirect URI rules (threat model S-03, S-05): exact absolute URIs, no wildcards, no fragments,
/// HTTPS except for loopback development hosts, and reverse-domain custom schemes for native apps (RFC 8252).
/// </summary>
public static class RedirectUriPolicy
{
    public static void EnsureAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException($"Redirect URI '{uri}' must be absolute.");
        }

        if (uri.OriginalString.Contains('*', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Redirect URI '{uri}' must not contain wildcards.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException($"Redirect URI '{uri}' must not contain a fragment.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps || IsLoopbackHttp(uri) || IsReverseDomainScheme(uri))
        {
            return;
        }

        throw new ArgumentException(
            $"Redirect URI '{uri}' must use HTTPS, loopback HTTP, or a reverse-domain custom scheme.");
    }

    public static void EnsureAllowedOrigin(Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (!origin.IsAbsoluteUri || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query))
        {
            throw new ArgumentException($"Web origin '{origin}' must be a scheme, host and optional port only.");
        }

        if (origin.OriginalString.Contains('*', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Web origin '{origin}' must not contain wildcards.");
        }

        if (origin.Scheme != Uri.UriSchemeHttps && !IsLoopbackHttp(origin))
        {
            throw new ArgumentException($"Web origin '{origin}' must use HTTPS or loopback HTTP.");
        }
    }

    /// <summary>Formats an origin the way browsers send it: scheme://host[:port] with no trailing slash.</summary>
    public static string ToOriginString(Uri origin) => origin.GetLeftPart(UriPartial.Authority);

    private static bool IsLoopbackHttp(Uri uri) => uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;

    private static bool IsReverseDomainScheme(Uri uri) =>
        uri.Scheme.Contains('.', StringComparison.Ordinal)
        && uri.Scheme != Uri.UriSchemeHttp
        && uri.Scheme != Uri.UriSchemeHttps;
}
