namespace Dovepeak.Identity.Platform.Common;

public enum PlatformErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    QuotaExceeded,
    Unavailable,
}

/// <summary>
/// An expected business error. The Management API maps it to an RFC 9457 problem response with a stable
/// machine-readable <see cref="Code"/>. Messages must never contain secrets or internal identifiers of other tenants.
/// </summary>
public sealed class PlatformException : Exception
{
    public PlatformException()
    {
    }

    public PlatformException(string message)
        : base(message)
    {
    }

    public PlatformException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PlatformException(PlatformErrorKind kind, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Kind = kind;
        Code = code;
        Errors = errors;
    }

    public PlatformErrorKind Kind { get; }

    public string Code { get; } = "error";

    /// <summary>Field-level validation errors, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    /// <summary>
    /// "Not found" is also returned for resources in other organizations, so callers cannot discover that they exist
    /// (threat model I-01).
    /// </summary>
    public static PlatformException NotFound(string resource) =>
        new(PlatformErrorKind.NotFound, $"{resource}_not_found", $"The {resource.Replace('_', ' ')} was not found.");

    public static PlatformException Forbidden(string permission) =>
        new(PlatformErrorKind.Forbidden, "permission_denied", $"This action requires the '{permission}' permission.");

    public static PlatformException Conflict(string code, string message) => new(PlatformErrorKind.Conflict, code, message);

    public static PlatformException Quota(string resource, int limit) =>
        new(PlatformErrorKind.QuotaExceeded, "quota_exceeded", $"The limit of {limit} {resource} has been reached.");

    public static PlatformException Invalid(string field, string message) =>
        new(PlatformErrorKind.Validation, "validation_failed", "The request is invalid.",
            new Dictionary<string, string[]> { [field] = [message] });

    public static PlatformException Unavailable(string message) =>
        new(PlatformErrorKind.Unavailable, "identity_engine_unavailable", message);
}
