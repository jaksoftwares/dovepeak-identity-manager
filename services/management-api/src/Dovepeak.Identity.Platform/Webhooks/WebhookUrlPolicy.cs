using System.Net;
using System.Net.Sockets;
using Dovepeak.Identity.Platform.Common;

namespace Dovepeak.Identity.Platform.Webhooks;

/// <summary>
/// Server-side request forgery protection for webhook destinations (threat model E-03).
/// URLs are checked when registered, and every resolved IP address is checked again at connection time,
/// which also defeats DNS rebinding.
/// </summary>
public static class WebhookUrlPolicy
{
    public static Uri Validate(string? url, bool allowLoopback)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || url.Length > 2048)
        {
            throw PlatformException.Invalid("url", "An absolute URL is required.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw PlatformException.Invalid("url", "Webhook URLs must not contain credentials or fragments.");
        }

        var loopbackHttp = allowLoopback && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !loopbackHttp)
        {
            throw PlatformException.Invalid("url", "Webhook URLs must use HTTPS.");
        }

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && !IsAllowed(literal, allowLoopback))
        {
            throw PlatformException.Invalid("url", "Webhook URLs must point to a public address.");
        }

        if (!allowLoopback && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
        {
            throw PlatformException.Invalid("url", "Webhook URLs must point to a public address.");
        }

        return uri;
    }

    public static bool IsAllowed(IPAddress address, bool allowLoopback)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return allowLoopback;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                     // "this" network
                || b[0] == 10                                       // private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)       // carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                     // link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)        // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // IETF protocol assignments
                || (b[0] == 192 && b[1] == 168)                     // private
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))      // benchmarking
                || b[0] >= 224);                                    // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC);                          // unique local fc00::/7
        }

        return false;
    }
}
