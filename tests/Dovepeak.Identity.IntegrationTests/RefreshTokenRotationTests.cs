using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M1.4 / ADR-0003 — refresh token rotation and reuse detection.</summary>
[Trait("Category", "Integration")]
public sealed class RefreshTokenRotationTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task Refresh_IssuesNewRefreshToken()
    {
        var (_, tokens) = await SignIn.NewUserAsync(realm, realm.WebApp);

        var refreshed = await realm.WebApp.RefreshAsync(tokens.RefreshToken!);

        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(tokens.AccessToken, refreshed.AccessToken);
    }

    [Fact]
    public async Task ReusingRotatedRefreshToken_IsRejected_AndRevokesTheWholeFamily()
    {
        var (_, tokens) = await SignIn.NewUserAsync(realm, realm.WebApp);
        var stolen = tokens.RefreshToken!;
        var legitimate = (await realm.WebApp.RefreshAsync(stolen)).RefreshToken!;

        // The attacker replays the old token.
        using var replay = await realm.WebApp.TryRefreshAsync(stolen);
        Assert.False(replay.IsSuccessStatusCode);
        Assert.Contains("invalid_grant", await replay.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Reuse is treated as theft: the legitimate holder's newest token is revoked too.
        using var afterReuse = await realm.WebApp.TryRefreshAsync(legitimate);
        Assert.False(afterReuse.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (_, tokens) = await SignIn.NewUserAsync(realm, realm.WebApp);

        await realm.WebApp.LogoutAsync(tokens.RefreshToken!);

        using var response = await realm.WebApp.TryRefreshAsync(tokens.RefreshToken!);
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AdminLogoutOfUser_RevokesAllTheirSessions()
    {
        var (user, first) = await SignIn.NewUserAsync(realm, realm.WebApp);
        var second = await SignIn.AsAsync(user, realm.Bff);
        Assert.Equal(2, (await TestRealm.Admin.GetUserSessionsAsync(realm.Name, user.Id!, CancellationToken.None)).Count);

        // Used by "log out everywhere" and after password reset or suspension (ADR-0003).
        await TestRealm.Admin.LogoutUserAsync(realm.Name, user.Id!, CancellationToken.None);

        using var webApp = await realm.WebApp.TryRefreshAsync(first.RefreshToken!);
        using var bff = await realm.Bff.TryRefreshAsync(second.RefreshToken!);
        Assert.False(webApp.IsSuccessStatusCode);
        Assert.False(bff.IsSuccessStatusCode);
        Assert.Empty(await TestRealm.Admin.GetUserSessionsAsync(realm.Name, user.Id!, CancellationToken.None));
    }

    [Fact]
    public async Task RevokingOneSession_LeavesOtherSessionsActive()
    {
        var (user, first) = await SignIn.NewUserAsync(realm, realm.WebApp);
        var second = await SignIn.AsAsync(user, realm.WebApp);
        var sessions = await TestRealm.Admin.GetUserSessionsAsync(realm.Name, user.Id!, CancellationToken.None);
        Assert.Equal(2, sessions.Count);

        var firstSessionId = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(first.AccessToken).Claims.Single(c => c.Type == "sid").Value;
        await TestRealm.Admin.DeleteSessionAsync(realm.Name, firstSessionId, CancellationToken.None);

        using var revoked = await realm.WebApp.TryRefreshAsync(first.RefreshToken!);
        using var active = await realm.WebApp.TryRefreshAsync(second.RefreshToken!);
        Assert.False(revoked.IsSuccessStatusCode);
        Assert.True(active.IsSuccessStatusCode);
    }
}
