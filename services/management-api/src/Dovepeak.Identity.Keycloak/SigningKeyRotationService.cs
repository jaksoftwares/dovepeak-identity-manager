using System.Globalization;
using System.Text.Json.Nodes;

namespace Dovepeak.Identity.Keycloak;

public sealed record KeyRotationResult(string NewKeyProviderId, IReadOnlyList<string> DeactivatedKeyProviderIds);

/// <summary>
/// Signing key lifecycle for a tenant realm (milestone M2.3, threat model I-07).
/// </summary>
/// <remarks>
/// <para><b>Planned rotation</b> adds a new RS256 key that signs all new tokens and moves previous keys to
/// <i>passive</i>: still published in JWKS so already-issued tokens validate until they expire.</para>
/// <para><b>Retirement</b> deletes passive keys once they have been passive longer than the longest access token
/// lifetime, so no valid token can depend on them.</para>
/// <para><b>Emergency rotation</b> replaces the RSA signing key and the HMAC key that protects refresh tokens,
/// deletes the old keys immediately and revokes every session. Every outstanding token becomes invalid.</para>
/// </remarks>
public sealed class SigningKeyRotationService(KeycloakAdminClient admin, TimeProvider timeProvider)
{
    private const string RsaProvider = "rsa-generated";
    private const string HmacProvider = "hmac-generated";
    private const string PassiveSinceMarker = "-passive-since-";
    private const int PriorityStep = 10;

    public async Task<KeyRotationResult> RotateAsync(RealmName realm, CancellationToken cancellationToken)
    {
        var providers = await admin.GetKeyProvidersAsync(realm, cancellationToken).ConfigureAwait(false);
        var rsaKeys = providers.Where(p => ProviderId(p) == RsaProvider).ToList();

        var newId = await admin.AddGeneratedKeyAsync(
            realm, RsaProvider, "RS256", $"rsa-{Timestamp()}", MaxPriority(rsaKeys) + PriorityStep, cancellationToken).ConfigureAwait(false);

        var deactivated = new List<string>();
        foreach (var key in rsaKeys.Where(IsActive))
        {
            var id = Id(key);
            await admin.SetKeyProviderStateAsync(realm, id, active: false, enabled: true, cancellationToken).ConfigureAwait(false);
            await RenameAsync(realm, key, $"{BaseName(key)}{PassiveSinceMarker}{timeProvider.GetUtcNow().ToUnixTimeSeconds()}", cancellationToken).ConfigureAwait(false);
            deactivated.Add(id);
        }

        return new KeyRotationResult(newId, deactivated);
    }

    /// <summary>Deletes RSA keys that have been passive for at least <paramref name="minimumPassiveAge"/>.</summary>
    /// <returns>IDs of the deleted key providers.</returns>
    public async Task<IReadOnlyList<string>> RetirePassiveKeysAsync(RealmName realm, TimeSpan minimumPassiveAge, CancellationToken cancellationToken)
    {
        var providers = await admin.GetKeyProvidersAsync(realm, cancellationToken).ConfigureAwait(false);
        var cutoff = timeProvider.GetUtcNow() - minimumPassiveAge;
        var retired = new List<string>();

        foreach (var key in providers.Where(p => ProviderId(p) == RsaProvider && !IsActive(p)))
        {
            if (PassiveSince(key) is { } since && since <= cutoff)
            {
                await admin.DeleteComponentAsync(realm, Id(key), cancellationToken).ConfigureAwait(false);
                retired.Add(Id(key));
            }
        }

        return retired;
    }

    /// <summary>
    /// Compromise response: new RSA and HMAC keys, immediate deletion of all previous ones, and revocation of
    /// every session. Resource servers must refresh their JWKS cache to stop accepting old access tokens.
    /// </summary>
    public async Task<KeyRotationResult> EmergencyRotateAsync(RealmName realm, CancellationToken cancellationToken)
    {
        var providers = await admin.GetKeyProvidersAsync(realm, cancellationToken).ConfigureAwait(false);
        var compromised = providers.Where(p => ProviderId(p) is RsaProvider or HmacProvider).ToList();
        var stamp = Timestamp();

        var newRsa = await admin.AddGeneratedKeyAsync(
            realm, RsaProvider, "RS256", $"rsa-{stamp}-emergency",
            MaxPriority(compromised.Where(p => ProviderId(p) == RsaProvider)) + PriorityStep, cancellationToken).ConfigureAwait(false);
        await admin.AddGeneratedKeyAsync(
            realm, HmacProvider, "HS512", $"hmac-{stamp}-emergency",
            MaxPriority(compromised.Where(p => ProviderId(p) == HmacProvider)) + PriorityStep, cancellationToken).ConfigureAwait(false);

        foreach (var key in compromised)
        {
            await admin.DeleteComponentAsync(realm, Id(key), cancellationToken).ConfigureAwait(false);
        }

        await admin.LogoutAllAsync(realm, cancellationToken).ConfigureAwait(false);

        return new KeyRotationResult(newRsa, compromised.Select(Id).ToList());
    }

    private async Task RenameAsync(RealmName realm, JsonObject key, string name, CancellationToken cancellationToken)
    {
        // Re-read: SetKeyProviderStateAsync has just updated this component.
        var current = (await admin.GetKeyProvidersAsync(realm, cancellationToken).ConfigureAwait(false)).Single(p => Id(p) == Id(key));
        current["name"] = name;
        await admin.UpdateComponentAsync(realm, Id(key), current, cancellationToken).ConfigureAwait(false);
    }

    private string Timestamp() => timeProvider.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Id(JsonObject provider) => provider["id"]!.GetValue<string>();

    private static string ProviderId(JsonObject provider) => provider["providerId"]!.GetValue<string>();

    private static string BaseName(JsonObject provider)
    {
        var name = provider["name"]!.GetValue<string>();
        var marker = name.IndexOf(PassiveSinceMarker, StringComparison.Ordinal);
        return marker < 0 ? name : name[..marker];
    }

    private static string? ConfigValue(JsonObject provider, string key) =>
        provider["config"]?[key]?.AsArray().FirstOrDefault()?.GetValue<string>();

    // Keycloak omits "active" and "enabled" when they hold their default value (true).
    private static bool IsActive(JsonObject provider) =>
        ConfigValue(provider, "active") != "false" && ConfigValue(provider, "enabled") != "false";

    private static long MaxPriority(IEnumerable<JsonObject> providers) =>
        providers.Select(p => long.Parse(ConfigValue(p, "priority") ?? "0", CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();

    private static DateTimeOffset? PassiveSince(JsonObject provider)
    {
        var name = provider["name"]!.GetValue<string>();
        var marker = name.IndexOf(PassiveSinceMarker, StringComparison.Ordinal);
        return marker >= 0 && long.TryParse(name[(marker + PassiveSinceMarker.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
