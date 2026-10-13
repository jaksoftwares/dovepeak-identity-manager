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

    /// <summary>
    /// Gives back what <see cref="ConsumeAsync"/> counted when the provider did not accept the message: quotas count
    /// email that went out, so a provider outage does not leave users throttled once it is over.
    /// </summary>
    Task ReleaseAsync(string realm, string recipient, CancellationToken cancellationToken);
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
        var (tenantKey, hourKey, dayKey) = Keys(realm, recipient);

        try
        {
            var db = valkey.GetDatabase();
            var tenantCount = await IncrementAsync(db, tenantKey, Hour).ConfigureAwait(false);
            var hourCount = await IncrementAsync(db, hourKey, Hour).ConfigureAwait(false);
            var dayCount = await IncrementAsync(db, dayKey, Day).ConfigureAwait(false);

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

    public async Task ReleaseAsync(string realm, string recipient, CancellationToken cancellationToken)
    {
        var (tenantKey, hourKey, dayKey) = Keys(realm, recipient);
        try
        {
            var db = valkey.GetDatabase();
            foreach (var key in new[] { tenantKey, hourKey, dayKey })
            {
                // Only an existing counter: a window that has rolled over since ConsumeAsync must not go negative.
                if (await db.KeyExistsAsync(key).ConfigureAwait(false))
                {
                    await db.StringDecrementAsync(key).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogQuotaUnavailable(logger, ex, realm);
        }
    }

    private (string Tenant, string Hour, string Day) Keys(string realm, string recipient)
    {
        var now = time.GetUtcNow();
        var hour = now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        var account = RecipientKey(realm, recipient);
        return ($"mailquota:tenant:{realm}:{hour}", $"mailquota:rcpt:{account}:{hour}",
            $"mailquota:rcpt:{account}:d{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
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
