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
        var realm = RealmName.Parse(args.Require("realm"));
        if (!args.Has("confirm"))
        {
            Console.Error.WriteLine("Emergency rotation revokes every session in the realm. Re-run with --confirm.");
            return 2;
        }

        var result = await services.GetRequiredService<SigningKeyRotationService>().EmergencyRotateAsync(realm, CancellationToken.None);
        Console.WriteLine($"New signing key provider: {result.NewKeyProviderId}");
        Console.WriteLine($"Deleted compromised key providers: {string.Join(", ", result.DeactivatedKeyProviderIds)}");
        Console.WriteLine("All sessions revoked. Resource servers must refresh their JWKS cache.");
        return 0;
    }
}
