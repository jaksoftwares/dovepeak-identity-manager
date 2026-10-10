using System.ComponentModel.DataAnnotations;

namespace Dovepeak.Identity.MailRelay;

/// <summary>The relay's listener and the shared secret realms sign in with.</summary>
public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    [Range(1, 65535)]
    public int Port { get; set; } = 2525;

    /// <summary>Password every realm uses; the realm name is the user name (SmtpOptions.RealmAsUsername).</summary>
    [Required]
    [MinLength(16)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Largest accepted message, in bytes. Identity emails are small.</summary>
    [Range(1024, 10 * 1024 * 1024)]
    public int MaxMessageBytes { get; set; } = 512 * 1024;
}

/// <summary>The real email provider messages are forwarded to.</summary>
public sealed class UpstreamOptions
{
    public const string SectionName = "Upstream";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>Require STARTTLS (production providers). Off only for a local capture server.</summary>
    public bool StartTls { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Sending quotas (known limitation L-04). Counted in fixed windows in Valkey, so they hold across relay instances.
/// A recipient is identified per realm: the same address in two tenants is two accounts.
/// </summary>
public sealed class QuotaOptions
{
    public const string SectionName = "Quotas";

    [Range(1, 10_000)]
    public int PerRecipientPerHour { get; set; } = 5;

    [Range(1, 100_000)]
    public int PerRecipientPerDay { get; set; } = 20;

    [Range(1, 10_000_000)]
    public int PerTenantPerHour { get; set; } = 500;
}
