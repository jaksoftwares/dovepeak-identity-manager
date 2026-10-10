using System.ComponentModel.DataAnnotations;

namespace Dovepeak.Identity.Keycloak;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Internal base URL used for admin API calls, e.g. http://keycloak:8080.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>Realm that holds the Management API service account.</summary>
    [Required]
    public string AdminRealm { get; set; } = "master";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>SMTP settings applied to every provisioned realm. Optional; email is disabled when Host is empty.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string From { get; set; } = "no-reply@dovepeak.local";

    public string FromDisplayName { get; set; } = "Dovepeak Identity";

    public bool Ssl { get; set; }

    public bool StartTls { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>
    /// Signs in to the SMTP server with the realm name as the user name (and <see cref="Password"/>), so the Dovepeak
    /// mail relay can apply per-tenant and per-recipient sending quotas. Ignored when <see cref="Username"/> is set.
    /// </summary>
    public bool RealmAsUsername { get; set; }

    public string? Password { get; set; }
}
