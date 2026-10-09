using System.Globalization;
using System.Text.RegularExpressions;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// A tenant's (project's) branding for hosted sign-in pages and emails (ADR-0010). Null values use the Dovepeak
/// platform defaults. Values reach Keycloak as realm localization texts, which the dovepeak login and email themes
/// read; the themes re-validate colours and URLs, and HTML-escape every value.
/// </summary>
public sealed partial record TenantBranding(
    string? LogoUrl = null,
    string? PrimaryColor = null,
    string? EmailVerificationSubject = null,
    string? EmailVerificationIntro = null,
    string? PasswordResetSubject = null,
    string? PasswordResetIntro = null)
{
    public const string Locale = "en";

    public const int MaxSubjectLength = 150;
    public const int MaxIntroLength = 1000;
    public const int MaxLogoUrlLength = 500;

    /// <summary>Dark text used on light brand colours (Dovepeak navy).</summary>
    public const string DarkText = "#000027";

    /// <summary>Realm localization keys the Management API owns; anything else in the realm is left alone.</summary>
    public static readonly IReadOnlyList<string> ManagedKeys =
    [
        "dovepeakBrandLogoUrl", "dovepeakBrandPrimaryColor", "dovepeakBrandPrimaryColorDark", "dovepeakBrandOnPrimaryColor",
        "emailVerificationSubject", "dovepeakEmailVerificationIntro", "passwordResetSubject", "dovepeakPasswordResetIntro",
    ];

    public static TenantBranding Default { get; } = new();

    /// <summary>Throws <see cref="ArgumentException"/> (message names the field) when a value is unsafe or unusable.</summary>
    public void Validate()
    {
        if (LogoUrl is not null)
        {
            if (LogoUrl.Length > MaxLogoUrlLength
                || !Uri.TryCreate(LogoUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || LogoUrl.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>'))
            {
                throw new ArgumentException($"logoUrl must be an absolute HTTPS URL without credentials (at most {MaxLogoUrlLength} characters).");
            }
        }

        if (PrimaryColor is not null)
        {
            if (!HexColor().IsMatch(PrimaryColor))
            {
                throw new ArgumentException("primaryColor must be a hex colour such as #1a73e8.");
            }
        }

        ValidateText(EmailVerificationSubject, "emailVerificationSubject", MaxSubjectLength, singleLine: true);
        ValidateText(PasswordResetSubject, "passwordResetSubject", MaxSubjectLength, singleLine: true);
        ValidateText(EmailVerificationIntro, "emailVerificationIntro", MaxIntroLength, singleLine: false);
        ValidateText(PasswordResetIntro, "passwordResetIntro", MaxIntroLength, singleLine: false);
    }

    /// <summary>Desired realm localization texts; a null value means "remove the override" (theme default applies).</summary>
    public IReadOnlyDictionary<string, string?> ToLocalizationTexts()
    {
        var primary = PrimaryColor?.ToLowerInvariant();
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["dovepeakBrandLogoUrl"] = LogoUrl,
            ["dovepeakBrandPrimaryColor"] = primary,
            ["dovepeakBrandPrimaryColorDark"] = primary is null ? null : Darken(primary, 0.15),
            ["dovepeakBrandOnPrimaryColor"] = primary is null ? null : TextColorOn(primary),
            ["emailVerificationSubject"] = MessageFormatEscape(EmailVerificationSubject),
            ["dovepeakEmailVerificationIntro"] = MessageFormatEscape(EmailVerificationIntro),
            ["passwordResetSubject"] = MessageFormatEscape(PasswordResetSubject),
            ["dovepeakPasswordResetIntro"] = MessageFormatEscape(PasswordResetIntro),
        };
    }

    /// <summary>
    /// Button text colour for a brand colour: white or Dovepeak navy, whichever contrasts more (WCAG 2.x), so any
    /// brand colour stays readable without rejecting it.
    /// </summary>
    public static string TextColorOn(string hex)
    {
        var luminance = Luminance(hex);
        var withWhite = 1.05 / (luminance + 0.05);
        var withDark = (luminance + 0.05) / (Luminance(DarkText) + 0.05);
        return withWhite >= withDark ? "#ffffff" : DarkText;
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(string hex)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var (r, g, b) = Rgb(hex);
        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    private static string Darken(string hex, double amount)
    {
        var (r, g, b) = Rgb(hex);
        int Scale(int c) => (int)Math.Round(c * (1 - amount));
        return string.Create(CultureInfo.InvariantCulture, $"#{Scale(r):x2}{Scale(g):x2}{Scale(b):x2}");
    }

    private static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static void ValidateText(string? value, string field, int maxLength, bool singleLine)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length == 0 || value.Length > maxLength)
        {
            throw new ArgumentException($"{field} must be 1–{maxLength} characters.");
        }

        // Line breaks in a subject would allow email header injection; other control characters are never needed.
        if (value.Any(c => char.IsControl(c) && (singleLine || c is not ('\n' or '\r'))))
        {
            throw new ArgumentException(singleLine ? $"{field} must be a single line." : $"{field} contains unsupported control characters.");
        }

        // Keycloak formats texts with java.text.MessageFormat: braces would be read as placeholders.
        if (value.Contains('{', StringComparison.Ordinal) || value.Contains('}', StringComparison.Ordinal))
        {
            throw new ArgumentException($"{field} must not contain {{ or }}.");
        }
    }

    /// <summary>MessageFormat treats a single quote as an escape character; a literal quote is written twice.</summary>
    private static string? MessageFormatEscape(string? value) => value?.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
