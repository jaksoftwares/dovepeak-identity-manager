using System.Text.Json;

namespace Dovepeak.Identity.Management;

#pragma warning disable CS1591 // Record members mirror the Management API's documented JSON fields (see /openapi/v1.json).

/// <summary>A project and its three environments.</summary>
public sealed record Project(Guid Id, string Slug, string Name, DateTimeOffset CreatedAt, IReadOnlyList<ProjectEnvironment> Environments)
{
    /// <summary>The environment of the given kind: <c>development</c>, <c>staging</c> or <c>production</c>.</summary>
    public ProjectEnvironment Environment(string kind) => Environments.Single(e => e.Kind == kind);
}

/// <summary>An isolated environment with its own users and issuer. <c>State</c> is <c>pending</c>, <c>ready</c>, <c>failed</c> or <c>deleting</c>.</summary>
public sealed record ProjectEnvironment(Guid Id, Guid ProjectId, string Kind, string State, string Issuer, DateTimeOffset CreatedAt)
{
    public bool IsReady => State == "ready";
}

/// <summary>Token and session lifetimes in seconds; null inherits the environment baseline.</summary>
public sealed record TokenPolicy(int? AccessTokenLifetimeSeconds = null, int? SessionIdleTimeoutSeconds = null, int? SessionMaxLifetimeSeconds = null);

/// <summary>An application (OAuth client). <c>Kind</c> is <c>spa</c>, <c>native</c>, <c>web</c> or <c>machine</c>.</summary>
public sealed record Application(
    Guid Id,
    Guid ProjectId,
    Guid EnvironmentId,
    string Name,
    string Kind,
    string ClientId,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> WebOrigins,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> Scopes,
    TokenPolicy TokenPolicy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Settings for a new application.</summary>
public sealed record NewApplication(
    string Name,
    string Kind,
    IReadOnlyList<string>? RedirectUris = null,
    IReadOnlyList<string>? PostLogoutRedirectUris = null,
    IReadOnlyList<string>? WebOrigins = null,
    IReadOnlyList<string>? Audiences = null,
    IReadOnlyList<string>? Scopes = null,
    TokenPolicy? TokenPolicy = null);

/// <summary>Changes to an application. Null fields are left unchanged; a supplied <c>TokenPolicy</c> replaces the whole policy.</summary>
public sealed record ApplicationChanges(
    string? Name = null,
    IReadOnlyList<string>? RedirectUris = null,
    IReadOnlyList<string>? PostLogoutRedirectUris = null,
    IReadOnlyList<string>? WebOrigins = null,
    IReadOnlyList<string>? Audiences = null,
    IReadOnlyList<string>? Scopes = null,
    TokenPolicy? TokenPolicy = null);

/// <summary>A new application. <c>ClientSecret</c> (web and machine apps) is returned only here: store it securely.</summary>
public sealed record CreatedApplication(Application Application, string? ClientSecret);

/// <summary>A new client secret. The previous one keeps working until <c>PreviousSecretExpiresAt</c> (null: stopped immediately).</summary>
public sealed record RotatedSecret(Application Application, string ClientSecret, DateTimeOffset? PreviousSecretExpiresAt);

/// <summary>Everything a client needs to integrate. Never contains secrets.</summary>
public sealed record ApplicationConfig(
    string ClientId,
    string Kind,
    string Issuer,
    string DiscoveryUrl,
    string TokenEndpointAuthMethod,
    bool PkceRequired,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> WebOrigins,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> Scopes,
    int AccessTokenLifetimeSeconds,
    int? SessionIdleTimeoutSeconds,
    int? SessionMaxLifetimeSeconds);

public sealed record Scope(string Name, string? Description, DateTimeOffset CreatedAt);

public sealed record Role(string Name, string? Description, DateTimeOffset CreatedAt);

public sealed record EndUser(string Id, string? Email, bool EmailVerified, bool Enabled, DateTimeOffset CreatedAt);

public sealed record AuditEvent(
    Guid Id,
    string Source,
    string Type,
    DateTimeOffset OccurredAt,
    string? ActorType,
    string? ActorId,
    string? ResourceType,
    string? ResourceId,
    Guid? EnvironmentId,
    string? ClientId,
    string? IpAddress,
    string? Error,
    JsonElement Details);

public sealed record AuditPage(IReadOnlyList<AuditEvent> Events, DateTimeOffset? NextBefore);

#pragma warning restore CS1591
