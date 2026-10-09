namespace Dovepeak.Identity.Persistence.Tenancy;

public enum OrganizationRole
{
    Viewer,
    Developer,
    Admin,
    Owner,
}

public enum EnvironmentKind
{
    Development,
    Staging,
    Production,
}

public enum ProvisioningState
{
    Pending,
    Ready,
    Failed,
    Deleting,
}

public enum ApplicationKind
{
    /// <summary>Single-page application (public client).</summary>
    Spa,

    /// <summary>Native mobile or desktop application (public client).</summary>
    Native,

    /// <summary>Server-side web application or BFF (confidential client).</summary>
    Web,

    /// <summary>Backend service using client credentials (confidential, no user login).</summary>
    Machine,
}

public sealed class Organization
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A developer's membership in an organization. Developers are identities in the platform realm.</summary>
public sealed class OrganizationMember : ITenantOwned
{
    public Guid OrganizationId { get; init; }

    /// <summary>Subject (<c>sub</c>) of the developer in the platform realm.</summary>
    public required string UserId { get; init; }

    public required string Email { get; set; }

    public OrganizationRole Role { get; set; }

    public DateTimeOffset JoinedAt { get; init; }
}

public sealed class OrganizationInvitation : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary>Lower-case email address. Accepted only by a developer whose verified email matches.</summary>
    public required string Email { get; init; }

    public OrganizationRole Role { get; init; }

    public required string CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class Project : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>Branding for hosted sign-in pages and emails in all of the project's environments (ADR-0010). Null uses platform defaults.</summary>
    public string? BrandLogoUrl { get; set; }

    public string? BrandPrimaryColor { get; set; }

    public string? EmailVerificationSubject { get; set; }

    public string? EmailVerificationIntro { get; set; }

    public string? PasswordResetSubject { get; set; }

    public string? PasswordResetIntro { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A project environment. Each one is an isolated identity directory (ADR-0002) backed by one realm.</summary>
public sealed class ProjectEnvironment : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ProjectId { get; init; }

    public EnvironmentKind Kind { get; init; }

    /// <summary>Identity engine realm. Internal: never part of the public API contract.</summary>
    public required string RealmName { get; init; }

    /// <summary>Identity engine cluster hosting the realm (tenant sharding, ADR-0001).</summary>
    public required string Cluster { get; init; }

    public ProvisioningState State { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Application : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ProjectId { get; init; }

    public Guid EnvironmentId { get; init; }

    public required string Name { get; set; }

    public ApplicationKind Kind { get; init; }

    /// <summary>Public OAuth client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>Identity engine's internal ID for the client.</summary>
    public string? EngineClientId { get; set; }

    public List<string> RedirectUris { get; set; } = [];

    public List<string> PostLogoutRedirectUris { get; set; } = [];

    public List<string> WebOrigins { get; set; } = [];

    public List<string> Audiences { get; set; } = [];

    /// <summary>Environment scopes this application may request; issued tokens carry them in the "scope" claim.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Token and session lifetimes in seconds; null inherits the realm baseline.</summary>
    public int? AccessTokenLifetimeSeconds { get; set; }

    public int? SessionIdleTimeoutSeconds { get; set; }

    public int? SessionMaxLifetimeSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An OAuth scope defined for one project environment (for example "orders:read").</summary>
public sealed class EnvironmentScope : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ProjectId { get; init; }

    public Guid EnvironmentId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; set; }

    /// <summary>Identity engine's internal ID for the client scope.</summary>
    public string? EngineScopeId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ApplicationRole : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid ApplicationId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Developer API key for automation against the Management API (ADR-0004).</summary>
public sealed class ApiKey : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    /// <summary>First characters of the key, safe to display (for example <c>dpk_live_a1b2c3</c>).</summary>
    public required string Prefix { get; init; }

    /// <summary>HMAC-SHA-256 of the full key with a server-side secret. The key itself is never stored.</summary>
    public required byte[] Digest { get; init; }

    public List<string> Scopes { get; init; } = [];

    public required string CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>Append-only record of an administrative change made through the Management API (threat model R-01).</summary>
public sealed class ManagementAuditEvent : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    /// <summary><c>user</c> or <c>api_key</c>.</summary>
    public required string ActorType { get; init; }

    public required string ActorId { get; init; }

    public required string Action { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public string Details { get; init; } = "{}";

    public string? IpAddress { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Work to perform reliably after a transaction commits (transactional outbox).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid? OrganizationId { get; init; }

    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class WebhookEndpoint : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public required string Url { get; init; }

    /// <summary>Signing secret, encrypted with ASP.NET Core Data Protection.</summary>
    public required string ProtectedSecret { get; init; }

    public List<string> EventTypes { get; init; } = [];

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }
}

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Failed,
}

public sealed class WebhookDelivery : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; init; }

    public Guid EndpointId { get; init; }

    public Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required string Payload { get; init; }

    public WebhookDeliveryStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>Stored response for a request made with an <c>Idempotency-Key</c> header.</summary>
public sealed class IdempotencyRecord
{
    /// <summary>Caller identity (developer or API key) the key belongs to.</summary>
    public required string Scope { get; init; }

    public required string Key { get; init; }

    public required string RequestHash { get; init; }

    public int StatusCode { get; init; }

    public required string ResponseBody { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
