using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>Just enough HTML inspection to navigate the hosted login pages.</summary>
public sealed partial record HtmlPage(Uri Url, HttpStatusCode StatusCode, string Body)
{
    public string Title => Decode(TitlePattern().Match(Body).Groups[1].Value).Trim();

    /// <summary>Visible feedback and field-level error messages, joined for assertions and diagnostics.</summary>
    public string Errors => string.Join(
        " | ",
        FeedbackPattern().Matches(Body).Concat(InputErrorPattern().Matches(Body))
            .Select(m => Decode(m.Groups[1].Value).Trim())
            .Where(text => text.Length > 0));

    /// <summary>The page's informational messages (for example "You should receive an email shortly").</summary>
    public string Text => Decode(TagPattern().Replace(Body, " "));

    public bool HasForm(string formId) => FormPattern(formId).IsMatch(Body);

    public Uri FormAction(string formId)
    {
        var match = FormPattern(formId).Match(Body);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Form '{formId}' not found on page '{Title}' ({Url}).");
        }

        return new Uri(Url, Decode(match.Groups[1].Value));
    }

    /// <summary>Finds the first link whose URL contains the given fragment, e.g. "reset-credentials".</summary>
    public Uri Link(string contains)
    {
        var href = LinkPattern().Matches(Body)
            .Select(m => Decode(m.Groups[1].Value))
            .FirstOrDefault(h => h.Contains(contains, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No link containing '{contains}' on page '{Title}' ({Url}).");

        return new Uri(Url, href);
    }

    private static string Decode(string value) => HttpUtility.HtmlDecode(value);

    private static Regex FormPattern(string formId) =>
        new($"<form[^>]*id=\"{Regex.Escape(formId)}\"[^>]*action=\"([^\"]+)\"|<form[^>]*action=\"([^\"]+)\"[^>]*id=\"{Regex.Escape(formId)}\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();

    [GeneratedRegex("class=\"[^\"]*kc-feedback-text[^\"]*\"[^>]*>(.*?)<", RegexOptions.Singleline)]
    private static partial Regex FeedbackPattern();

    [GeneratedRegex("id=\"input-error[^\"]*\"[^>]*>(.*?)<", RegexOptions.Singleline)]
    private static partial Regex InputErrorPattern();

    [GeneratedRegex("href=\"([^\"]+)\"")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
