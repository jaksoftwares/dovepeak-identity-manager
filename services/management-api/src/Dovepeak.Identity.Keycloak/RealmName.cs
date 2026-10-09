using System.Text.RegularExpressions;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// A validated Keycloak realm name. Realm names appear in token issuer URLs, so they are opaque
/// identifiers derived from environment IDs rather than organization or project names (ADR-0001).
/// </summary>
public sealed partial record RealmName
{
    private RealmName(string value) => Value = value;

    public string Value { get; }

    public static RealmName Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!ValidPattern().IsMatch(value))
        {
            throw new ArgumentException(
                "Realm names must be 3–63 characters of lowercase letters, digits and hyphens, starting with a letter.",
                nameof(value));
        }

        if (string.Equals(value, "master", StringComparison.Ordinal))
        {
            throw new ArgumentException("The master realm cannot be managed as a tenant realm.", nameof(value));
        }

        return new RealmName(value);
    }

    /// <summary>Creates the realm name for a project environment.</summary>
    public static RealmName ForEnvironment(Guid environmentId) => new($"dp-{environmentId:N}");

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9-]{2,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidPattern();
}
