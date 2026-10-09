using Dovepeak.Identity.IntegrationTests.Infrastructure;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestones M2.2 and M2.6 — self-registration, password policy and email verification.</summary>
[Trait("Category", "Integration")]
public sealed class RegistrationAndVerificationTests(TestRealm realm) : IClassFixture<TestRealm>
{
    [Fact]
    public async Task Registration_RequiresEmailVerification_ThenSignsIn()
    {
        var user = TestUser.Generate();
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var registrationPage = await OpenRegistrationAsync(browser, request);
        var afterRegister = await browser.SubmitAsync(registrationPage, "kc-register-form", RegistrationFields(user));

        // No tokens until the email address is proven.
        Assert.Null(afterRegister.CallbackUri);
        Assert.Contains("verify your email", afterRegister.Page!.Text, StringComparison.OrdinalIgnoreCase);

        var email = await Mailpit.WaitForMessageAsync(user.Email, "Verify your email");
        var callback = (await browser.GetAsync(email.ActionLink())).RequireCallback();
        var tokens = await realm.WebApp.ExchangeCodeAsync(callback.RequireCode(request), request.CodeVerifier);

        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(tokens.IdToken).Claims.ToList();
        Assert.Equal(user.Email, claims.Single(c => c.Type == "email").Value);
        Assert.Equal("true", claims.Single(c => c.Type == "email_verified").Value);
    }

    [Theory]
    [InlineData("Short-1", "minimum length")]
    public async Task Registration_EnforcesPasswordPolicy(string password, string expectedError)
    {
        var user = TestUser.Generate() with { Password = password };
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var registrationPage = await OpenRegistrationAsync(browser, request);
        var result = await browser.SubmitAsync(registrationPage, "kc-register-form", RegistrationFields(user));

        Assert.Null(result.CallbackUri);
        Assert.Contains(expectedError, result.Page!.Errors, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registration_RejectsPasswordEqualToEmail()
    {
        var user = TestUser.Generate();
        user = user with { Password = user.Email };
        var request = realm.WebApp.CreateAuthorizationRequest();
        using var browser = new BrowserSession(TestRealm.RedirectUri);

        var registrationPage = await OpenRegistrationAsync(browser, request);
        var result = await browser.SubmitAsync(registrationPage, "kc-register-form", RegistrationFields(user));

        Assert.Null(result.CallbackUri);
        Assert.NotEmpty(result.Page!.Errors);
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

    private static async Task<HtmlPage> OpenRegistrationAsync(BrowserSession browser, AuthorizationRequest request)
    {
        var loginPage = (await browser.GetAsync(request.Url)).RequirePage();
        return (await browser.GetAsync(loginPage.Link("/login-actions/registration"))).RequirePage();
    }

    private static Dictionary<string, string> RegistrationFields(TestUser user) => new()
    {
        ["email"] = user.Email,
        ["password"] = user.Password,
        ["password-confirm"] = user.Password,
    };
}
