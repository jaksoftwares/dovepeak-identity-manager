using System.Net;
using System.Text.RegularExpressions;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Scripted browser for driving the hosted login pages over HTTP: keeps cookies, follows redirects,
/// and stops when the identity provider redirects back to the application's callback.
/// </summary>
/// <remarks>
/// Cookies are tracked manually because Keycloak marks its cookies <c>Secure</c> even on
/// http://localhost (browsers treat localhost as a secure context; HttpClient's CookieContainer does not).
/// </remarks>
public sealed partial class BrowserSession : IDisposable
{
    private const int MaxRedirects = 10;

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly Uri _callbackUri;

    public BrowserSession(Uri callbackUri) => _callbackUri = callbackUri;

    public Task<Navigation> GetAsync(Uri url) => SendAsync(HttpMethod.Get, url, form: null);

    public Task<Navigation> SubmitAsync(HtmlPage page, string formId, IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(page);
        return SendAsync(HttpMethod.Post, page.FormAction(formId), fields);
    }

    /// <summary>Runs the authorization request and signs in, returning the callback (with code or error).</summary>
    public async Task<Uri> SignInAsync(AuthorizationRequest request, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var loginPage = (await GetAsync(request.Url)).RequirePage();
        var result = await SubmitAsync(loginPage, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = user.Email,
            ["password"] = user.Password,
        });

        return result.RequireCallback();
    }

    public void Dispose() => _http.Dispose();

    private async Task<Navigation> SendAsync(HttpMethod method, Uri url, IReadOnlyDictionary<string, string>? form)
    {
        for (var hop = 0; hop < MaxRedirects; hop++)
        {
            if (IsCallback(url))
            {
                return Navigation.ToCallback(url);
            }

            using var request = new HttpRequestMessage(method, url);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }

            using var response = await _http.SendAsync(request);
            StoreCookies(response);

            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                url = new Uri(url, response.Headers.Location!);
                method = HttpMethod.Get;
                form = null;
                continue;
            }

            var body = await response.Content.ReadAsStringAsync();
            return Navigation.ToPage(new HtmlPage(url, response.StatusCode, body));
        }

        throw new InvalidOperationException($"Too many redirects starting at {url}.");
    }

    private bool IsCallback(Uri url) =>
        url.GetLeftPart(UriPartial.Path).Equals(_callbackUri.GetLeftPart(UriPartial.Path), StringComparison.Ordinal);

    private void StoreCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return;
        }

        foreach (var header in headers)
        {
            var nameValue = header.Split(';', 2)[0];
            var separator = nameValue.IndexOf('=', StringComparison.Ordinal);
            var name = nameValue[..separator].Trim();
            var value = nameValue[(separator + 1)..].Trim();

            if (value.Length == 0 || ExpiredCookie().IsMatch(header))
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = value;
            }
        }
    }

    [GeneratedRegex("Max-Age=0(;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiredCookie();
}

public sealed record Navigation(Uri? CallbackUri, HtmlPage? Page)
{
    public static Navigation ToCallback(Uri uri) => new(uri, null);

    public static Navigation ToPage(HtmlPage page) => new(null, page);

    public HtmlPage RequirePage() =>
        Page ?? throw new InvalidOperationException($"Expected a page but was redirected to {CallbackUri}.");

    public Uri RequireCallback() =>
        CallbackUri ?? throw new InvalidOperationException(
            $"Expected a redirect to the application but got page '{Page!.Title}' ({Page.Url}). Errors: {Page.Errors}");
}
