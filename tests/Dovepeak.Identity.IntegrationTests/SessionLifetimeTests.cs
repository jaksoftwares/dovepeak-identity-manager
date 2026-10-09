using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// Milestone M2.6 / ADR-0003 — sessions end at their absolute lifetime regardless of activity.
/// Uses a realm configured with a 15-second maximum session lifetime.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SessionLifetimeTests(ShortLivedRealm realm) : IClassFixture<ShortLivedRealm>
{
    [Fact]
    public async Task Session_ExpiresAtAbsoluteLifetime_EvenWhenActive()
    {
        var (_, tokens) = await SignIn.NewUserAsync(realm, realm.WebApp);

        // Keep the session active by refreshing.
        await Task.Delay(TimeSpan.FromSeconds(6));
        var refreshed = await realm.WebApp.RefreshAsync(tokens.RefreshToken!);

        await Task.Delay(TimeSpan.FromSeconds(11));
        using var afterMaxLifetime = await realm.WebApp.TryRefreshAsync(refreshed.RefreshToken!);

        Assert.False(afterMaxLifetime.IsSuccessStatusCode);
    }

    [Fact]
    public async Task RefreshTokenLifetime_NeverExceedsSessionLifetime()
    {
        var (_, tokens) = await SignIn.NewUserAsync(realm, realm.WebApp);

        Assert.InRange(tokens.RefreshExpiresIn, 1, 15);
        Assert.InRange(tokens.ExpiresIn, 1, 5);
    }
}
