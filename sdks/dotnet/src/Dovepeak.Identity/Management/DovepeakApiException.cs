using System.Net;

namespace Dovepeak.Identity.Management;

/// <summary>
/// The Management API refused a request. <see cref="Code"/> is the stable problem code (for example
/// <c>quota_exceeded</c>, <c>validation_failed</c>, <c>scope_in_use</c>); resources of other organizations are
/// reported as <see cref="HttpStatusCode.NotFound"/>.
/// </summary>
public sealed class DovepeakApiException : Exception
{
    /// <summary>Creates an exception for a problem response.</summary>
    public DovepeakApiException(HttpStatusCode statusCode, string? code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    /// <summary>Creates an exception with only a message.</summary>
    public DovepeakApiException()
        : this(HttpStatusCode.InternalServerError, null, "The Management API request failed.")
    {
    }

    /// <summary>Creates an exception with only a message.</summary>
    public DovepeakApiException(string message)
        : this(HttpStatusCode.InternalServerError, null, message)
    {
    }

    /// <summary>Creates an exception wrapping another.</summary>
    public DovepeakApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = HttpStatusCode.InternalServerError;
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>The HTTP status of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The stable problem code, when the API sent one.</summary>
    public string? Code { get; }

    /// <summary>Validation errors by field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
