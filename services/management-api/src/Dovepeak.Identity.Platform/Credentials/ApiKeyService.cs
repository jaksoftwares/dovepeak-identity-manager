using System.Security.Cryptography;
using System.Text;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Credentials;

public sealed record ApiKeyView(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt);

/// <summary>Returned once at creation. The key cannot be retrieved again (ADR-0004).</summary>
public sealed record CreatedApiKey(ApiKeyView ApiKey, string Key);

/// <summary>
/// Developer API key format and digest (ADR-0004): <c>dpk_live_</c> + 43 base62 characters (256 bits).
/// Only an HMAC-SHA-256 digest keyed with a server secret is stored, so a database leak does not expose usable keys.
/// </summary>
public sealed class ApiKeyCodec(IOptions<PlatformOptions> options)
{
    public const string Prefix = "dpk_live_";
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int RandomLength = 43;

    private readonly byte[] _digestKey = DecodeKey(options.Value.ApiKeyDigestKey);

    public static bool LooksLikeKey(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Generate()
    {
        var chars = new char[RandomLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
        }

        return Prefix + new string(chars);
    }

    public byte[] Digest(string key) => HMACSHA256.HashData(_digestKey, Encoding.UTF8.GetBytes(key));

    /// <summary>The non-secret part shown in listings, enough to recognise a key.</summary>
    public static string DisplayPrefix(string key) => key[..(Prefix.Length + 6)];

    private static byte[] DecodeKey(string base64)
    {
        var key = Convert.FromBase64String(base64);
        return key.Length >= 32 ? key : throw new InvalidOperationException("Platform:ApiKeyDigestKey must decode to at least 32 bytes.");
    }
}

/// <summary>API key management (milestone M3.6).</summary>
public sealed class ApiKeyService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ICallerAccessor callerAccessor,
    ManagementAuditLog audit,
    ApiKeyCodec codec,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(366);

    public async Task<CreatedApiKey> CreateAsync(Guid organizationId, string name, IReadOnlyList<string> scopes, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ApiKeysManage, ct);
        name = Slug.ValidateName(name, nameof(name));

        var permissions = new HashSet<Permission>();
        foreach (var scope in scopes)
        {
            if (!Permissions.TryParseScope(scope, out var permission) || Permissions.HumanOnly.Contains(permission))
            {
                throw PlatformException.Invalid("scopes", $"'{scope}' is not a scope that API keys can hold.");
            }

            // A key can never hold more than its creator.
            if (!access.Permissions.Contains(permission))
            {
                throw PlatformException.Forbidden(scope);
            }

            permissions.Add(permission);
        }

        if (permissions.Count == 0)
        {
            throw PlatformException.Invalid("scopes", "At least one scope is required.");
        }

        var now = timeProvider.GetUtcNow();
        if (expiresAt is { } expiry && (expiry <= now || expiry > now + MaxLifetime))
        {
            throw PlatformException.Invalid("expiresAt", "Expiry must be in the future and within 366 days.");
        }

        var limit = options.Value.Quotas.ApiKeysPerOrganization;
        if (await db.ApiKeys.CountAsync(k => k.RevokedAt == null, ct) >= limit)
        {
            throw PlatformException.Quota("active API keys", limit);
        }

        var key = ApiKeyCodec.Generate();
        var entity = new ApiKey
        {
            OrganizationId = organizationId,
            Name = name,
            Prefix = ApiKeyCodec.DisplayPrefix(key),
            Digest = codec.Digest(key),
            Scopes = permissions.Select(p => p.ToScope()).Order(StringComparer.Ordinal).ToList(),
            CreatedBy = callerAccessor.Caller.Id,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        db.ApiKeys.Add(entity);
        audit.Record(organizationId, "api_key.created", "api_key", entity.Id, new { name, entity.Prefix, entity.Scopes, expiresAt });
        await db.SaveChangesAsync(ct);

        return new CreatedApiKey(ToView(entity), key);
    }

    public async Task<IReadOnlyList<ApiKeyView>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApiKeysManage, ct);
        var keys = await db.ApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        return keys.Select(ToView).ToList();
    }

    /// <summary>Revocation takes effect on the next request: keys are validated against the database every time.</summary>
    public async Task RevokeAsync(Guid organizationId, Guid keyId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApiKeysManage, ct);
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == keyId, ct) ?? throw PlatformException.NotFound("api_key");
        if (key.RevokedAt is null)
        {
            key.RevokedAt = timeProvider.GetUtcNow();
            audit.Record(organizationId, "api_key.revoked", "api_key", key.Id, new { key.Prefix });
            await db.SaveChangesAsync(ct);
        }
    }

    private static ApiKeyView ToView(ApiKey k) =>
        new(k.Id, k.Name, k.Prefix, k.Scopes, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt);
}

/// <summary>Validates presented API keys for the Management API's authentication handler.</summary>
public sealed class ApiKeyValidator(PlatformDbContext db, ApiKeyCodec codec, TimeProvider timeProvider)
{
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    public async Task<ApiKey?> ValidateAsync(string presented, CancellationToken ct)
    {
        if (!ApiKeyCodec.LooksLikeKey(presented) || presented.Length > 100)
        {
            return null;
        }

        var digest = codec.Digest(presented);
        var now = timeProvider.GetUtcNow();

        db.TenantScope.EnterSystem();
        try
        {
            var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Digest == digest, ct);
            if (key is null || key.RevokedAt is not null || (key.ExpiresAt is { } expiry && expiry <= now))
            {
                return null;
            }

            if (key.LastUsedAt is null || now - key.LastUsedAt > LastUsedResolution)
            {
                key.LastUsedAt = now;
                await db.SaveChangesAsync(ct);
            }

            return key;
        }
        finally
        {
            db.TenantScope.ExitSystem();
        }
    }
}
