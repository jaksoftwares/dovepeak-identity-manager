using Polly;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// Surfaces resilience-pipeline rejections (attempt or total timeout, open circuit) as <see cref="HttpRequestException"/>,
/// the exception callers already treat as "the identity engine is unavailable; retry later".
/// </summary>
internal sealed class ResilienceExceptionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new HttpRequestException($"The identity engine did not respond in time ({ex.GetType().Name}).", ex);
        }
    }
}
