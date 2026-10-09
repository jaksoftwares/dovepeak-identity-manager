using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestones M2.2, M2.4 and M2.6 — password recovery by email, enumeration resistance and session revocation.</summary>
[Trait("Category", "Integration")]
public sealed class PasswordRecoveryTests(TestRealm realm) : IClassFixture<TestRealm>
{
    private const string RecoveryConfirmation = "You should receive an email shortly";

    [Fact]
    public async Task PasswordReset_ChangesPassword_AndRevokesExistingSessions()
    {
        var (user, existingSession) = await SignIn.NewUserAsync(realm, realm.WebApp);
        var newPassword = $"Reset-{Guid.NewGuid():N}";
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var confirmation = await RequestResetAsync(browser, request, user.Email);
        Assert.Contains(RecoveryConfirmation, confirmation.Text, StringComparison.Ordinal);

        var email = await Mailpit.WaitForMessageAsync(user.Email, "Reset password");
        var updatePage = (await browser.GetAsync(email.ActionLink())).RequirePage();
        var callback = (await browser.SubmitAsync(updatePage, "kc-passwd-update-form", new Dictionary<string, string>
        {
            ["password-new"] = newPassword,
            ["password-confirm"] = newPassword,
            ["logout-sessions"] = "on",
        })).RequireCallback();
        await realm.WebApp.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);

        // ADR-0003: a password reset revokes every other session.
        using var oldSession = await realm.WebApp.TryRefreshAsync(existingSession.RefreshToken!);
        Assert.False(oldSession.IsSuccessStatusCode);

        // Old password no longer works; new one does.
        await Assert.ThrowsAsync<InvalidOperationException>(() => SignIn.AsAsync(user, realm.WebApp));
        await SignIn.AsAsync(user with { Password = newPassword }, realm.WebApp);
    }

    [Fact]
    public async Task PasswordReset_ForUnknownEmail_LooksIdentical_AndSendsNothing()
    {
        // Threat model I-02: recovery must not reveal whether an account exists.
        var unknown = $"nobody-{Guid.NewGuid():N}@example.test";
        var known = await realm.CreateUserAsync();
        using var browserA = new BrowserSession(TestRealm.RedirectUri);
        using var browserB = new BrowserSession(TestRealm.RedirectUri);

        var forUnknown = await RequestResetAsync(browserA, realm.WebApp.CreateAuthorizationRequest(), unknown);
        var forKnown = await RequestResetAsync(browserB, realm.WebApp.CreateAuthorizationRequest(), known.Email);

        Assert.Contains(RecoveryConfirmation, forUnknown.Text, StringComparison.Ordinal);
        Assert.Contains(RecoveryConfirmation, forKnown.Text, StringComparison.Ordinal);
        Assert.Equal(forKnown.StatusCode, forUnknown.StatusCode);

        await Mailpit.WaitForMessageAsync(known.Email, "Reset password");
        Assert.Equal(0, await Mailpit.CountMessagesAsync(unknown));
    }

    [Fact]
    public async Task ResetLink_CanOnlyBeUsedOnce()
    {
        var user = await realm.CreateUserAsync();
        var newPassword = $"Reset-{Guid.NewGuid():N}";
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        await RequestResetAsync(browser, request, user.Email);
        var link = (await Mailpit.WaitForMessageAsync(user.Email, "Reset password")).ActionLink();

        var updatePage = (await browser.GetAsync(link)).RequirePage();
        (await browser.SubmitAsync(updatePage, "kc-passwd-update-form", new Dictionary<string, string>
        {
            ["password-new"] = newPassword,
            ["password-confirm"] = newPassword,
        })).RequireCallback();

        using var attacker = new BrowserSession(TestRealm.RedirectUri);
        var replay = (await attacker.GetAsync(link)).RequirePage();
        Assert.False(replay.HasForm("kc-passwd-update-form"));
    }

    private static async Task<HtmlPage> RequestResetAsync(BrowserSession browser, AuthorizationRequest request, string email)
    {
        var loginPage = (await browser.GetAsync(request.Url)).RequirePage();
        var resetPage = (await browser.GetAsync(loginPage.Link("/login-actions/reset-credentials"))).RequirePage();
        return (await browser.SubmitAsync(resetPage, "kc-reset-password-form", new Dictionary<string, string>
        {
            ["username"] = email,
        })).RequirePage();
    }
}
