using System.Text.RegularExpressions;

namespace Dovepeak.Identity.Platform.Common;

public static partial class Slug
{
    /// <summary>Validates a URL-safe identifier: 2–63 lowercase letters, digits and single hyphens.</summary>
    public static string Validate(string? value, string field)
    {
        if (value is null || !Pattern().IsMatch(value))
        {
            throw PlatformException.Invalid(field,
                "Use 2–63 lowercase letters, digits and hyphens, starting with a letter and not ending with a hyphen.");
        }

        return value;
    }

    public static string ValidateName(string? value, string field, int maxLength = 200)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maxLength)
        {
            throw PlatformException.Invalid(field, $"A name of 1–{maxLength} characters is required.");
        }

        return trimmed;
    }

    [GeneratedRegex("^[a-z](?:[a-z0-9]|-(?=[a-z0-9])){1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
