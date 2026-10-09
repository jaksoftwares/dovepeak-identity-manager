using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M2.4 — per-account brute-force protection (threat model S-01).</summary>
[Trait("Category", "Integration")]
public sealed class BruteForceProtectionTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task RepeatedFailures_LockTheAccount_EvenForTheCorrectPassword()
    {
        var user = await realm.CreateUserAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await AttemptAsync(user.Email, "wrong-password");
        }

        var withCorrectPassword = await AttemptAsync(user.Email, user.Password);

        Assert.Null(withCorrectPassword.CallbackUri);
        // The lockout is not disclosed: the response looks like any other failed sign-in.
        Assert.Contains("Invalid username or password", withCorrectPassword.Page!.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lockout_IsRecordedAsSecurityEvent()
    {
        var user = await realm.CreateUserAsync();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await AttemptAsync(user.Email, "wrong-password");
        }

        var events = await TestRealm.Admin.GetEventsAsync(realm.Name, 0, 100, CancellationToken.None);
        var forUser = events.Where(e => e["userId"]?.GetValue<string>() == user.Id).ToList();

        Assert.Contains(forUser, e => e["type"]!.GetValue<string>() == "LOGIN_ERROR");
        Assert.Contains(forUser, e => e["type"]!.GetValue<string>() == "USER_DISABLED_BY_TEMPORARY_LOCKOUT"
            || e["error"]?.GetValue<string>() == "user_temporarily_disabled");
    }

    private async Task<Navigation> AttemptAsync(string email, string password)
    {
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        var page = (await browser.GetAsync(request.Url)).RequirePage();
        return await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = email,
            ["password"] = password,
        });
    }
}
