using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Dovepeak.Identity.Platform.Webhooks;

/// <summary>
/// Webhook signatures (threat model T-03). Header format:
/// <c>Dovepeak-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA-256 of "{t}.{body}"&gt;</c>.
/// Receivers recompute the HMAC with their endpoint secret, compare in constant time, and reject timestamps
/// older than five minutes to prevent replay.
/// </summary>
public static class WebhookSigner
{
    public const string SignatureHeader = "Dovepeak-Signature";
    public const string EventIdHeader = "Dovepeak-Event-Id";
    public const string EventTypeHeader = "Dovepeak-Event-Type";
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static string NewSecret() => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Sign(string secret, long timestamp, string body)
    {
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return string.Create(CultureInfo.InvariantCulture, $"t={timestamp},v1={Convert.ToHexStringLower(signature)}");
    }

    /// <summary>Reference verification, as receivers (and the SDKs) should implement it.</summary>
    public static bool Verify(string secret, string header, string body, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !parts.TryGetValue("v1", out var v1)
            || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            return false;
        }

        if ((now - DateTimeOffset.FromUnixTimeSeconds(timestamp)).Duration() > Tolerance)
        {
            return false;
        }

        var expected = Sign(secret, timestamp, body).Split("v1=")[1];
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(v1));
    }
}
