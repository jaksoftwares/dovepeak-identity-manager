namespace Dovepeak.Identity.Persistence.Audit;

public static class AuditSources
{
    /// <summary>End-user authentication events (login, logout, token, recovery).</summary>
    public const string Authentication = "authentication";

    /// <summary>Administrative changes made through the identity engine's admin API.</summary>
    public const string Admin = "admin";
}

/// <summary>
/// An append-only record of a security-relevant event (threat model R-01, R-02).
/// Never stores credentials, tokens or authorization codes.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The tenant realm. Mapped to organization, project and environment in Phase 3.</summary>
    public required string Realm { get; init; }

    public required string Source { get; init; }

    /// <summary>Identifier assigned by the originating system, used for de-duplication.</summary>
    public required string SourceEventId { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset RecordedAt { get; init; }

    public string? UserId { get; init; }

    public string? ClientId { get; init; }

    public string? SessionId { get; init; }

    public string? IpAddress { get; init; }

    public string? Error { get; init; }

    /// <summary>Allow-listed event details as a JSON object.</summary>
    public string Details { get; init; } = "{}";
}

/// <summary>Collection progress per realm and source, so collection resumes where it stopped.</summary>
public sealed class AuditCheckpoint
{
    public required string Realm { get; init; }

    public required string Source { get; init; }

    public DateTimeOffset LastEventAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
