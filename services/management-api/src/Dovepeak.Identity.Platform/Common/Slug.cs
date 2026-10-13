using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dovepeak.Identity.Platform.Common;

public static partial class Slug
{
    private const int MaxLength = 63;
    private const int NumberedCandidates = 20;

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

    /// <summary>
    /// Derives a valid identifier from a display name ("Café Rouge Ltd." → "cafe-rouge-ltd"). Accents are removed,
    /// other characters become hyphens; a name that yields nothing usable gets <paramref name="fallback"/>.
    /// </summary>
    public static string FromName(string name, string fallback)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower))
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 0 && !char.IsAsciiLetterLower(slug[0]))
        {
            slug = $"{fallback}-{slug}";
        }

        slug = Truncate(slug, MaxLength);
        return Pattern().IsMatch(slug) ? slug : fallback;
    }

    /// <summary>
    /// The first identifier derived from <paramref name="name"/> that <paramref name="isTaken"/> reports free:
    /// "shop", then "shop-2" … "shop-20", then "shop-" with a random suffix.
    /// </summary>
    public static async Task<string> FirstAvailableAsync(string name, string fallback, Func<string, Task<bool>> isTaken)
    {
        var baseSlug = FromName(name, fallback);
        if (!await isTaken(baseSlug).ConfigureAwait(false))
        {
            return baseSlug;
        }

        for (var n = 2; n <= NumberedCandidates; n++)
        {
            var candidate = WithSuffix(baseSlug, n.ToString(CultureInfo.InvariantCulture));
            if (!await isTaken(candidate).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return WithSuffix(baseSlug, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)));
    }

    private static string WithSuffix(string slug, string suffix) =>
        $"{Truncate(slug, MaxLength - suffix.Length - 1)}-{suffix}";

    private static string Truncate(string slug, int length) => slug.Length <= length ? slug : slug[..length].TrimEnd('-');

    [GeneratedRegex("^[a-z](?:[a-z0-9]|-(?=[a-z0-9])){1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
