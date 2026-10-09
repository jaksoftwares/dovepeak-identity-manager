using Dovepeak.Identity.Platform.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Dovepeak.Identity.ManagementApi.Infrastructure;

/// <summary>
/// Maps expected platform errors to RFC 9457 problem responses with a stable <c>code</c>. Unexpected exceptions fall
/// through to the default handler, which returns a generic 500 without internal details.
/// </summary>
internal sealed class PlatformExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public const string ProblemTypeBase = "https://docs.dovepeak.io/problems/";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not PlatformException platform)
        {
            return false;
        }

        var status = platform.Kind switch
        {
            PlatformErrorKind.Validation => StatusCodes.Status400BadRequest,
            PlatformErrorKind.NotFound => StatusCodes.Status404NotFound,
            PlatformErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            PlatformErrorKind.Conflict or PlatformErrorKind.QuotaExceeded => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Type = ProblemTypeBase + platform.Code.Replace('_', '-'),
            Title = platform.Message,
        };
        problem.Extensions["code"] = platform.Code;
        if (platform.Errors is not null)
        {
            problem.Extensions["errors"] = platform.Errors;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
