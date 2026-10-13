using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// Milestones M2.2 and M2.6 — self-registration, password policy and email verification. Registration asks only for
/// the email address and profile; the password is chosen after the address is verified (Keycloak 26.5+ default), so
/// nobody can pre-register someone else's address with a password they control (account pre-hijacking).
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistrationAndVerificationTests(TestRealm realm) : IClassFixture<TestRealm>
{
    private const string PasswordForm = "kc-passwd-update-form";

    [Fact]
    public async Task Registration_VerifiesEmailFirst_ThenSetsPassword_ThenSignsIn()
    {
        var user = TestUser.Generate();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var registrationPage = await OpenRegistrationAsync(browser, request);
        Assert.DoesNotContain("name=\"password\"", registrationPage.Body, StringComparison.Ordinal);
        var afterRegister = await browser.SubmitAsync(registrationPage, "kc-register-form", new Dictionary<string, string> { ["email"] = user.Email });

        // No tokens until the email address is proven.
        Assert.Null(afterRegister.CallbackUri);
        Assert.Contains("verify your email", afterRegister.Page!.Text, StringComparison.OrdinalIgnoreCase);

        var email = await Mailpit.WaitForMessageAsync(user.Email, "Verify your email");
        var passwordPage = (await browser.GetAsync(email.ActionLink())).RequirePage();
        Assert.True(passwordPage.HasForm(PasswordForm), "After verification the user chooses a password.");

        var callback = (await browser.SubmitAsync(passwordPage, PasswordForm, PasswordFields(user.Password))).RequireCallback();
        var tokens = await realm.WebApp.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);

        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(tokens.IdToken).Claims.ToList();
        Assert.Equal(user.Email, claims.Single(c => c.Type == "email").Value);
        Assert.Equal("true", claims.Single(c => c.Type == "email_verified").Value);

        // The chosen password works for the next sign-in.
        await SignIn.AsAsync(user, realm.WebApp);
    }

    [Theory]
    [InlineData("Short-1", "minimum length")]
    public async Task Registration_EnforcesPasswordPolicy(string password, string expectedError)
    {
        var (browser, passwordPage, _) = await RegisterAndVerifyAsync();
        using (browser)
        {
            var result = await browser.SubmitAsync(passwordPage, PasswordForm, PasswordFields(password));

            Assert.Null(result.CallbackUri);
            Assert.Contains(expectedError, result.Page!.Errors, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Registration_RejectsPasswordEqualToEmail()
    {
        var (browser, passwordPage, user) = await RegisterAndVerifyAsync();
        using (browser)
        {
            var result = await browser.SubmitAsync(passwordPage, PasswordForm, PasswordFields(user.Email));

            Assert.Null(result.CallbackUri);
            Assert.NotEmpty(result.Page!.Errors);
        }
    }

    [Fact]
    public async Task UnverifiedUser_CannotSignIn_UntilVerified()
    {
        var user = await realm.CreateUserAsync(emailVerified: false);
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var page = (await browser.GetAsync(request.Url)).RequirePage();
        var result = await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string>
        {
            ["username"] = user.Email,
            ["password"] = user.Password,
        });

        Assert.Null(result.CallbackUri);
        var email = await Mailpit.WaitForMessageAsync(user.Email, "Verify your email");
        Assert.NotNull((await browser.GetAsync(email.ActionLink())).CallbackUri);
    }

    /// <summary>Registers a new user and follows the verification link to the "choose a password" page.</summary>
    private async Task<(BrowserSession Browser, HtmlPage PasswordPage, TestUser User)> RegisterAndVerifyAsync()
    {
        var user = TestUser.Generate();
        var request = realm.WebApp.CreateAuthorizationRequest();
        var browser = new BrowserSession(TestRealm.RedirectUri);

        var registrationPage = await OpenRegistrationAsync(browser, request);
        await browser.SubmitAsync(registrationPage, "kc-register-form", new Dictionary<string, string> { ["email"] = user.Email });
        var email = await Mailpit.WaitForMessageAsync(user.Email, "Verify your email");
        return (browser, (await browser.GetAsync(email.ActionLink())).RequirePage(), user);
    }

    private static async Task<HtmlPage> OpenRegistrationAsync(BrowserSession browser, AuthorizationRequest request)
    {
        var loginPage = (await browser.GetAsync(request.Url)).RequirePage();
        return (await browser.GetAsync(loginPage.Link("/login-actions/registration"))).RequirePage();
    }

    private static Dictionary<string, string> PasswordFields(string password) => new()
    {
        ["password-new"] = password,
        ["password-confirm"] = password,
    };
}
