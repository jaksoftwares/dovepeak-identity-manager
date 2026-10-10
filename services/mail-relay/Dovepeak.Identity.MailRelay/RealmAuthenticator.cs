using System.Security.Cryptography;
using System.Text;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.Options;
using SmtpServer;
using SmtpServer.Authentication;

namespace Dovepeak.Identity.MailRelay;

/// <summary>
/// Realms sign in with their realm name as the user name and the shared relay password. The authenticated realm is kept
/// on the session so quotas apply per tenant. Unauthenticated sessions cannot send (the relay is never an open relay).
/// </summary>
public sealed class RealmAuthenticator(IOptions<RelayOptions> options) : IUserAuthenticator
{
    public const string RealmProperty = "dovepeak.realm";

    public Task<bool> AuthenticateAsync(ISessionContext context, string user, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Password));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual) || !RealmName.TryParse(user, out var realm))
        {
            return Task.FromResult(false);
        }

        context.Properties[RealmProperty] = realm.Value;
        return Task.FromResult(true);
    }
}
