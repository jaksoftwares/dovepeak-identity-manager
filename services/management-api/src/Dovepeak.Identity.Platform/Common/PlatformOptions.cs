using System.ComponentModel.DataAnnotations;

namespace Dovepeak.Identity.Platform.Common;

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    /// <summary>Public base URL of the identity endpoints (the edge), used to build issuer URLs.</summary>
    [Required]
    public Uri? PublicIdentityUrl { get; set; }

    /// <summary>Realm holding developer accounts for the portal and Management API.</summary>
    [Required]
    public string PlatformRealm { get; set; } = "dovepeak-platform";

    /// <summary>Audience the Management API requires in developer access tokens.</summary>
    [Required]
    public string ManagementApiAudience { get; set; } = "dovepeak-management-api";

    /// <summary>Client used by the developer portal (and tests) to sign developers in.</summary>
    [Required]
    public string PortalClientId { get; set; } = "dovepeak-portal";

    public List<Uri> PortalRedirectUris { get; set; } = [];

    /// <summary>Identity engine cluster that new environments are placed on (tenant sharding, ADR-0001).</summary>
    [Required]
    public string DefaultCluster { get; set; } = "default";

    /// <summary>Base64-encoded secret (at least 32 bytes) used to compute API key digests.</summary>
    [Required]
    [MinLength(44)]
    public string ApiKeyDigestKey { get; set; } = string.Empty;

    public QuotaOptions Quotas { get; set; } = new();

    public WebhookOptions Webhooks { get; set; } = new();
}

public sealed class QuotaOptions
{
    [Range(1, 1000)]
    public int OrganizationsPerDeveloper { get; set; } = 5;

    [Range(1, 1000)]
    public int ProjectsPerOrganization { get; set; } = 10;

    [Range(1, 1000)]
    public int ApplicationsPerEnvironment { get; set; } = 20;

    [Range(1, 1000)]
    public int ApiKeysPerOrganization { get; set; } = 25;

    [Range(1, 100)]
    public int WebhooksPerOrganization { get; set; } = 10;

    [Range(1, 500)]
    public int ScopesPerEnvironment { get; set; } = 50;
}

public sealed class WebhookOptions
{
    /// <summary>
    /// Allows webhook URLs on loopback addresses over HTTP. Local development and tests only:
    /// in production, webhooks must use HTTPS to public addresses (threat model E-03).
    /// </summary>
    public bool AllowLoopback { get; set; }
}
