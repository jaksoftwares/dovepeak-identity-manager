using System.Net;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Credentials;
using Dovepeak.Identity.Platform.Security;
using Dovepeak.Identity.Platform.Webhooks;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.ManagementApi.Tests.Platform;

public sealed class PermissionMatrixTests
{
    [Fact]
    public void Roles_AreStrictlyNested()
    {
        var viewer = Permissions.For(OrganizationRole.Viewer);
        var developer = Permissions.For(OrganizationRole.Developer);
        var admin = Permissions.For(OrganizationRole.Admin);
        var owner = Permissions.For(OrganizationRole.Owner);

        Assert.True(viewer.IsProperSubsetOf(developer));
        Assert.True(developer.IsProperSubsetOf(admin));
        Assert.True(admin.IsProperSubsetOf(owner));
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer, Permission.ProjectsWrite, false)]
    [InlineData(OrganizationRole.Developer, Permission.ApplicationsWrite, true)]
    [InlineData(OrganizationRole.Developer, Permission.MembersManage, false)]
    [InlineData(OrganizationRole.Developer, Permission.AuditRead, false)]
    [InlineData(OrganizationRole.Admin, Permission.ApiKeysManage, true)]
    [InlineData(OrganizationRole.Admin, Permission.OrganizationManage, false)]
    [InlineData(OrganizationRole.Owner, Permission.OrganizationManage, true)]
    public void Role_GrantsExpectedPermission(OrganizationRole role, Permission permission, bool expected) =>
        Assert.Equal(expected, Permissions.For(role).Contains(permission));

    [Fact]
    public void EveryPermission_HasAUniqueScope()
    {
        var scopes = Enum.GetValues<Permission>().Select(p => p.ToScope()).ToList();
        Assert.Equal(scopes.Count, scopes.Distinct().Count());
        Assert.All(scopes, s => Assert.True(Permissions.TryParseScope(s, out _)));
    }
}

public sealed class ApiKeyCodecTests
{
    private static readonly ApiKeyCodec Codec = new(Options.Create(new PlatformOptions
    {
        ApiKeyDigestKey = Convert.ToBase64String(new byte[32]),
    }));

    [Fact]
    public void GeneratedKeys_HavePrefix_AndEnoughEntropy()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => ApiKeyCodec.Generate()).ToList();

        Assert.All(keys, k => Assert.StartsWith("dpk_live_", k, StringComparison.Ordinal));
        Assert.All(keys, k => Assert.Equal(52, k.Length));
        Assert.Equal(100, keys.Distinct().Count());
    }

    [Fact]
    public void Digest_IsDeterministic_AndKeyed()
    {
        var key = ApiKeyCodec.Generate();
        var other = new ApiKeyCodec(Options.Create(new PlatformOptions { ApiKeyDigestKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()) }));

        Assert.Equal(Codec.Digest(key), Codec.Digest(key));
        Assert.NotEqual(Codec.Digest(key), other.Digest(key));
    }

    [Fact]
    public void DigestKey_ShorterThan32Bytes_IsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ApiKeyCodec(Options.Create(new PlatformOptions { ApiKeyDigestKey = Convert.ToBase64String(new byte[16]) })));
}

public sealed class WebhookSignerTests
{
    [Fact]
    public void ValidSignature_Verifies()
    {
        var secret = WebhookSigner.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var header = WebhookSigner.Sign(secret, now.ToUnixTimeSeconds(), """{"type":"x"}""");

        Assert.True(WebhookSigner.Verify(secret, header, """{"type":"x"}""", now));
    }

    [Fact]
    public void TamperedBody_WrongSecret_OrReplayedTimestamp_FailVerification()
    {
        var secret = WebhookSigner.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var header = WebhookSigner.Sign(secret, now.ToUnixTimeSeconds(), "body");

        Assert.False(WebhookSigner.Verify(secret, header, "tampered", now));
        Assert.False(WebhookSigner.Verify(WebhookSigner.NewSecret(), header, "body", now));
        Assert.False(WebhookSigner.Verify(secret, header, "body", now.AddMinutes(6)));
        Assert.False(WebhookSigner.Verify(secret, "garbage", "body", now));
    }
}

public sealed class WebhookUrlPolicyTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void InternalAddresses_AreBlocked(string address) =>
        Assert.False(WebhookUrlPolicy.IsAllowed(IPAddress.Parse(address), allowLoopback: false));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicAddresses_AreAllowed(string address) =>
        Assert.True(WebhookUrlPolicy.IsAllowed(IPAddress.Parse(address), allowLoopback: false));

    [Fact]
    public void Loopback_IsAllowedOnlyWhenExplicitlyEnabled()
    {
        Assert.True(WebhookUrlPolicy.IsAllowed(IPAddress.Loopback, allowLoopback: true));
        WebhookUrlPolicy.Validate("http://127.0.0.1:5000/hook", allowLoopback: true);
        Assert.Throws<PlatformException>(() => WebhookUrlPolicy.Validate("http://127.0.0.1:5000/hook", allowLoopback: false));
        Assert.Throws<PlatformException>(() => WebhookUrlPolicy.Validate("https://localhost/hook", allowLoopback: false));
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not a url")]
    public void InvalidUrls_AreRejected(string url) =>
        Assert.Throws<PlatformException>(() => WebhookUrlPolicy.Validate(url, allowLoopback: false));
}

public sealed class SlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-shop-2")]
    public void ValidSlugs_AreAccepted(string slug) => Assert.Equal(slug, Slug.Validate(slug, "slug"));

    [Theory]
    [InlineData("a")]
    [InlineData("Acme")]
    [InlineData("acme-")]
    [InlineData("acme--shop")]
    [InlineData("1acme")]
    [InlineData("acme shop")]
    public void InvalidSlugs_AreRejected(string slug) => Assert.Throws<PlatformException>(() => Slug.Validate(slug, "slug"));
}
