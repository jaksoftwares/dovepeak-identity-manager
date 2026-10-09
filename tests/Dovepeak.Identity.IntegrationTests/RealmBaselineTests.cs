using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// Milestone M2.1 — automated configuration check: a provisioned realm must carry every security default
/// in the version-controlled template, exactly as Keycloak reports it back.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealmBaselineTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Theory]
    [InlineData("sslRequired", "external")]
    [InlineData("passwordPolicy", "length(12) and maxLength(128) and notUsername and notEmail and passwordHistory(5) and hashAlgorithm(argon2)")]
    [InlineData("bruteForceProtected", true)]
    [InlineData("permanentLockout", false)]
    [InlineData("failureFactor", 5)]
    [InlineData("verifyEmail", true)]
    [InlineData("registrationEmailAsUsername", true)]
    [InlineData("duplicateEmailsAllowed", false)]
    [InlineData("rememberMe", false)]
    [InlineData("defaultSignatureAlgorithm", "RS256")]
    [InlineData("accessTokenLifespan", 600)]
    [InlineData("ssoSessionIdleTimeout", 1800)]
    [InlineData("ssoSessionMaxLifespan", 43200)]
    [InlineData("revokeRefreshToken", true)]
    [InlineData("refreshTokenMaxReuse", 0)]
    [InlineData("eventsEnabled", true)]
    [InlineData("adminEventsEnabled", true)]
    [InlineData("adminEventsDetailsEnabled", false)]
    public async Task Realm_HasSecureDefault(string setting, object expected)
    {
        var representation = await TestRealm.Admin.GetRealmAsync(realm.Name, CancellationToken.None);

        var actual = representation![setting];
        Assert.NotNull(actual);
        Assert.Equal(expected.ToString(), actual.ToJsonString().Trim('"'), ignoreCase: true);
    }

    [Fact]
    public async Task Realm_MatchesEveryScalarSettingInTemplate()
    {
        var representation = await TestRealm.Admin.GetRealmAsync(realm.Name, CancellationToken.None);
        var drift = new List<string>();

        foreach (var (key, expected) in KeycloakAdmin.Template.Baseline())
        {
            if (expected is JsonValue && representation![key] is { } actual && actual.ToJsonString() != expected.ToJsonString())
            {
                drift.Add($"{key}: expected {expected.ToJsonString()}, got {actual.ToJsonString()}");
            }
        }

        Assert.Empty(drift);
    }

    [Fact]
    public async Task Realm_StoresSecurityEventTypes()
    {
        var representation = await TestRealm.Admin.GetRealmAsync(realm.Name, CancellationToken.None);
        var types = representation!["enabledEventTypes"]!.AsArray().Select(t => t!.GetValue<string>()).ToHashSet();

        Assert.Contains("LOGIN_ERROR", types);
        Assert.Contains("REFRESH_TOKEN_ERROR", types);
        Assert.Contains("USER_DISABLED_BY_TEMPORARY_LOCKOUT", types);
        Assert.Contains("RESET_PASSWORD", types);
    }

    [Fact]
    public async Task Realm_SendsSecurityHeaders()
    {
        var representation = await TestRealm.Admin.GetRealmAsync(realm.Name, CancellationToken.None);
        var headers = representation!["browserSecurityHeaders"]!;

        Assert.Equal("DENY", headers["xFrameOptions"]!.GetValue<string>());
        Assert.Contains("frame-ancestors 'none'", headers["contentSecurityPolicy"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
