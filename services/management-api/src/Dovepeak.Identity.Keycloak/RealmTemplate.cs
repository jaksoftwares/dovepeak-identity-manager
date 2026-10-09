using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// The version-controlled secure realm baseline from identity/keycloak/realm-template, embedded at build time.
/// </summary>
public sealed class RealmTemplate
{
    private const string TemplateResource = "Dovepeak.Identity.Keycloak.realm-template.json";
    private const string UserProfileResource = "Dovepeak.Identity.Keycloak.user-profile.json";

    private readonly JsonObject _template;
    private readonly JsonObject _userProfile;

    private RealmTemplate(JsonObject template, JsonObject userProfile)
    {
        _template = template;
        _userProfile = userProfile;
    }

    public string Version => _template["attributes"]?["dovepeak.templateVersion"]?.GetValue<string>()
        ?? throw new InvalidOperationException("Realm template has no version attribute.");

    public static RealmTemplate LoadEmbedded() =>
        new(LoadResource(TemplateResource), LoadResource(UserProfileResource));

    /// <summary>Returns a copy of the baseline, for configuration checks.</summary>
    public JsonObject Baseline() => (JsonObject)_template.DeepClone();

    /// <summary>
    /// The declarative user profile applied to every realm. Only email is required; names are optional so users
    /// are not forced through an "update profile" step after registration.
    /// </summary>
    public JsonObject UserProfile() => (JsonObject)_userProfile.DeepClone();

    public JsonObject Build(RealmName realm, string displayName, SmtpOptions smtp)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(smtp);

        var representation = (JsonObject)_template.DeepClone();
        representation["realm"] = realm.Value;
        representation["displayName"] = displayName;

        // Shown in the hosted login page header. Always HTML-encoded: display names come from tenants.
        representation["displayNameHtml"] = WebUtility.HtmlEncode(displayName);

        if (!string.IsNullOrWhiteSpace(smtp.Host))
        {
            representation["smtpServer"] = BuildSmtp(smtp);
        }

        return representation;
    }

    private static JsonObject LoadResource(string name)
    {
        using var stream = typeof(RealmTemplate).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");

        return JsonNode.Parse(stream) as JsonObject
            ?? throw new InvalidOperationException($"Embedded resource '{name}' must be a JSON object.");
    }

    private static JsonObject BuildSmtp(SmtpOptions smtp)
    {
        // Keycloak expects every SMTP setting as a string.
        var server = new JsonObject
        {
            ["host"] = smtp.Host,
            ["port"] = smtp.Port.ToString(CultureInfo.InvariantCulture),
            ["from"] = smtp.From,
            ["fromDisplayName"] = smtp.FromDisplayName,
            ["ssl"] = smtp.Ssl ? "true" : "false",
            ["starttls"] = smtp.StartTls ? "true" : "false",
            ["auth"] = string.IsNullOrEmpty(smtp.Username) ? "false" : "true",
        };

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            server["user"] = smtp.Username;
            server["password"] = smtp.Password;
        }

        return server;
    }
}
