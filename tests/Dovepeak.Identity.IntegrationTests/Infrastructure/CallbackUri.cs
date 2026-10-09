using System.Web;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

public static class CallbackUri
{
    public static string? Parameter(this Uri callback, string name)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return HttpUtility.ParseQueryString(callback.Query)[name];
    }

    /// <summary>Returns the authorization code after verifying the state parameter (CSRF protection).</summary>
    public static string RequireCode(this Uri callback, AuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var error = callback.Parameter("error");
        if (error is not null)
        {
            throw new InvalidOperationException($"Authorization failed: {error} — {callback.Parameter("error_description")}");
        }

        if (callback.Parameter("state") != request.State)
        {
            throw new InvalidOperationException("State mismatch in authorization response.");
        }

        return callback.Parameter("code") ?? throw new InvalidOperationException("No authorization code in callback.");
    }
}
