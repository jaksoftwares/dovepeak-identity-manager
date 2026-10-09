using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;

namespace Dovepeak.Identity.IntegrationTests.Infrastructure;

/// <summary>Reads messages captured by Mailpit (local SMTP sink).</summary>
public static partial class Mailpit
{
    private static readonly HttpClient Http = new() { BaseAddress = StackSettings.Current.MailpitUrl };
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<MailMessage> WaitForMessageAsync(string recipient, string subjectContains)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var query = Uri.EscapeDataString($"to:\"{recipient}\"");
            var search = await Http.GetFromJsonAsync<SearchResult>(new Uri($"api/v1/search?query={query}", UriKind.Relative));
            var summary = search?.Messages.FirstOrDefault(m => m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
            if (summary is not null)
            {
                var message = await Http.GetFromJsonAsync<MessageDetail>(new Uri($"api/v1/message/{summary.Id}", UriKind.Relative));
                return new MailMessage(summary.Subject, message!.Html, message.Text);
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"No email to {recipient} with subject containing '{subjectContains}' within {Timeout}.");
    }

    public static async Task<int> CountMessagesAsync(string recipient)
    {
        var query = Uri.EscapeDataString($"to:\"{recipient}\"");
        var search = await Http.GetFromJsonAsync<SearchResult>(new Uri($"api/v1/search?query={query}", UriKind.Relative));
        return search?.Messages.Count ?? 0;
    }

    public sealed partial record MailMessage(string Subject, string Html, string Text)
    {
        /// <summary>The Keycloak action link (email verification, password reset) in the message.</summary>
        public Uri ActionLink()
        {
            var match = ActionLinkPattern().Match(Html);
            if (!match.Success)
            {
                throw new InvalidOperationException($"No action link in email '{Subject}'.");
            }

            return new Uri(HttpUtility.HtmlDecode(match.Groups[1].Value));
        }

        [GeneratedRegex("href=\"([^\"]*/login-actions/action-token[^\"]*)\"")]
        private static partial Regex ActionLinkPattern();
    }

    private sealed record SearchResult([property: JsonPropertyName("messages")] IReadOnlyList<MessageSummary> Messages);

    private sealed record MessageSummary(
        [property: JsonPropertyName("ID")] string Id,
        [property: JsonPropertyName("Subject")] string Subject);

    private sealed record MessageDetail(
        [property: JsonPropertyName("HTML")] string Html,
        [property: JsonPropertyName("Text")] string Text);
}
