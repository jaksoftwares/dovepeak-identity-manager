namespace Dovepeak.Identity.Platform.Security;

public enum CallerKind
{
    /// <summary>A developer signed in to the platform realm.</summary>
    Developer,

    /// <summary>Automation using a developer API key.</summary>
    ApiKey,

    /// <summary>The platform itself (background jobs such as provisioning and reconciliation).</summary>
    System,
}

/// <summary>The authenticated caller of a Management API operation.</summary>
public sealed record Caller
{
    public required CallerKind Kind { get; init; }

    /// <summary>Developer subject, or API key ID.</summary>
    public required string Id { get; init; }

    public string? Email { get; init; }

    public bool EmailVerified { get; init; }

    /// <summary>For API keys: the only organization the key can access.</summary>
    public Guid? ApiKeyOrganizationId { get; init; }

    /// <summary>For API keys: the scopes granted when the key was created.</summary>
    public IReadOnlySet<Permission> ApiKeyPermissions { get; init; } = new HashSet<Permission>();

    public string? IpAddress { get; init; }

    public string ActorType => Kind switch
    {
        CallerKind.Developer => "user",
        CallerKind.ApiKey => "api_key",
        _ => "system",
    };

    public static Caller System { get; } = new() { Kind = CallerKind.System, Id = "dovepeak-platform" };

    /// <summary>Identifies the caller for idempotency records and auditing.</summary>
    public string ScopeKey => $"{ActorType}:{Id}";

    public void RequireDeveloper()
    {
        if (Kind != CallerKind.Developer)
        {
            throw new Common.PlatformException(Common.PlatformErrorKind.Forbidden, "developer_required",
                "This operation is only available to signed-in developers, not API keys.");
        }
    }
}

/// <summary>Supplies the caller of the current unit of work.</summary>
public interface ICallerAccessor
{
    Caller Caller { get; }
}

/// <summary>Caller for background jobs.</summary>
public sealed class SystemCallerAccessor : ICallerAccessor
{
    public Caller Caller => Caller.System;
}
