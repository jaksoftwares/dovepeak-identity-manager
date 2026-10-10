using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Dovepeak.Identity.MailRelay;

public enum QuotaDecision
{
    Allowed,
    RecipientLimit,
    TenantLimit,
}

public interface IEmailQuota
{
    /// <summary>Counts one message to <paramref name="recipient"/> from <paramref name="realm"/> and decides whether it may be sent.</summary>
    Task<QuotaDecision> ConsumeAsync(string realm, string recipient, CancellationToken cancellationToken);
}

/// <summary>Fixed-window counters in Valkey. Fails open: an unreachable cache must not stop verification and recovery mail.</summary>
public sealed partial class ValkeyEmailQuota(
    IConnectionMultiplexer valkey, IOptions<QuotaOptions> options, TimeProvider time, ILogger<ValkeyEmailQuota> logger) : IEmailQuota
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public async Task<QuotaDecision> ConsumeAsync(string realm, string recipient, CancellationToken cancellationToken)
    {
        var limits = options.Value;
        var now = time.GetUtcNow();
        var hour = now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var account = RecipientKey(realm, recipient);

        try
        {
            var db = valkey.GetDatabase();
            var tenantCount = await IncrementAsync(db, $"mailquota:tenant:{realm}:{hour}", Hour).ConfigureAwait(false);
            var hourCount = await IncrementAsync(db, $"mailquota:rcpt:{account}:{hour}", Hour).ConfigureAwait(false);
            var dayCount = await IncrementAsync(db, $"mailquota:rcpt:{account}:d{day}", Day).ConfigureAwait(false);

            if (tenantCount > limits.PerTenantPerHour)
            {
                return QuotaDecision.TenantLimit;
            }

            return hourCount > limits.PerRecipientPerHour || dayCount > limits.PerRecipientPerDay
                ? QuotaDecision.RecipientLimit
                : QuotaDecision.Allowed;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogQuotaUnavailable(logger, ex, realm);
            return QuotaDecision.Allowed;
        }
    }

    /// <summary>A stable, non-reversible key for an account: addresses never appear in the cache or the logs.</summary>
    internal static string RecipientKey(string realm, string recipient)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(realm + "\n" + recipient.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }

    private static async Task<long> IncrementAsync(IDatabase db, string key, TimeSpan window)
    {
        var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, window).ConfigureAwait(false);
        }

        return count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email quotas unavailable for realm {Realm}; sending without a quota check")]
    private static partial void LogQuotaUnavailable(ILogger logger, Exception exception, string realm);
}
