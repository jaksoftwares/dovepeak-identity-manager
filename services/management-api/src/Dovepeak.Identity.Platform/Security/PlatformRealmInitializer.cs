using System.Net;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Platform.Common;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Security;

/// <summary>
/// Ensures the platform realm that holds developer accounts exists, with the portal client whose tokens the
/// Management API accepts (milestone M3.1). Idempotent: safe to run on every start.
/// </summary>
public sealed class PlatformRealmInitializer(KeycloakAdminClient engine, IOptions<PlatformOptions> options)
{
    public async Task EnsureAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var realm = RealmName.Parse(settings.PlatformRealm);

        await engine.CreateRealmAsync(realm, "Dovepeak Identity", ct);

        var portal = new ClientRegistration(settings.PortalClientId, ClientKind.Public)
        {
            Name = "Dovepeak developer portal",
            RedirectUris = settings.PortalRedirectUris,
            PostLogoutRedirectUris = settings.PortalRedirectUris.Select(u => new Uri(u.GetLeftPart(UriPartial.Authority) + "/")).Distinct().ToList(),
            WebOrigins = settings.PortalRedirectUris.Select(u => new Uri(u.GetLeftPart(UriPartial.Authority))).Distinct().ToList(),
            Audiences = [settings.ManagementApiAudience],
        };

        // Several API replicas (or a replica and an operator tool) may run this at the same moment. Keycloak answers a
        // concurrent update of the same client with 409; the other writer applied the same desired state, so re-read
        // and converge rather than fail start-up.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var client = await engine.CreateClientAsync(realm, portal, ct);
                if (client.Outcome == ProvisioningOutcome.AlreadyExists)
                {
                    await engine.UpdateClientAsync(realm, client.Id, portal, ct);
                }

                return;
            }
            catch (KeycloakAdminException ex) when (ex.StatusCode == HttpStatusCode.Conflict && attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
        }
    }

    private const int MaxAttempts = 5;
}
