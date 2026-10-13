using System.Globalization;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.DevTool;

/// <summary>Signing key operations used by the key rotation runbook (docs/runbooks/signing-key-rotation.md).</summary>
internal static class KeyCommands
{
    public static async Task<int> RotateAsync(IServiceProvider services, CommandArgs args)
    {
        var realm = RealmName.Parse(args.Require("realm"));
        var result = await services.GetRequiredService<SigningKeyRotationService>().RotateAsync(realm, CancellationToken.None);

        Console.WriteLine($"New active key provider: {result.NewKeyProviderId}");
        Console.WriteLine($"Moved to passive: {string.Join(", ", result.DeactivatedKeyProviderIds)}");
        Console.WriteLine("Run retire-keys after the overlap period (at least the longest access token lifetime).");
        return 0;
    }

    public static async Task<int> RetireAsync(IServiceProvider services, CommandArgs args)
    {
        var realm = RealmName.Parse(args.Require("realm"));
        var minimumAge = TimeSpan.Parse(args.Get("min-age") ?? "00:15:00", CultureInfo.InvariantCulture);
        var retired = await services.GetRequiredService<SigningKeyRotationService>()
            .RetirePassiveKeysAsync(realm, minimumAge, CancellationToken.None);

        Console.WriteLine(retired.Count == 0 ? "No passive keys old enough to retire." : $"Retired: {string.Join(", ", retired)}");
        return 0;
    }

    public static async Task<int> EmergencyRotateAsync(IServiceProvider services, CommandArgs args)
    {
        if (!args.Has("confirm"))
        {
            Console.Error.WriteLine("Emergency rotation revokes every session in the realm. Re-run with --confirm.");
            return 2;
        }

        // --all: platform-wide compromise (for example a leaked Keycloak database). The management account only sees the
        // realms it created, so this covers every tenant realm and the platform realm, never master.
        var realms = args.Has("all")
            ? (await services.GetRequiredService<KeycloakAdminClient>().ListRealmNamesAsync(CancellationToken.None)).Select(RealmName.Parse).ToList()
            : [RealmName.Parse(args.Require("realm"))];

        var rotation = services.GetRequiredService<SigningKeyRotationService>();
        var failed = new List<string>();
        foreach (var realm in realms)
        {
            try
            {
                var result = await rotation.EmergencyRotateAsync(realm, CancellationToken.None);
                Console.WriteLine($"{realm}: new signing key provider {result.NewKeyProviderId}; deleted {string.Join(", ", result.DeactivatedKeyProviderIds)}");
            }
            catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException)
            {
                // Keep going: one unreachable realm must not leave the others on compromised keys.
                failed.Add(realm.Value);
                Console.Error.WriteLine($"{realm}: FAILED ({ex.Message})");
            }
        }

        Console.WriteLine($"Rotated {realms.Count - failed.Count} of {realms.Count} realm(s); all their sessions are revoked.");
        Console.WriteLine("Resource servers must refresh their JWKS cache.");
        if (failed.Count > 0)
        {
            Console.Error.WriteLine($"Re-run for: {string.Join(" ", failed)}");
            return 1;
        }

        return 0;
    }
}
