using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>
/// Milestone M4.3 (ADR-0010): per-tenant branding on hosted pages and in emails, tenant email templates and
/// security alert emails, verified on what Keycloak actually renders and sends.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class BrandingApiTests(ManagementApiFixture api)
{
    private const string LogoUrl = "https://cdn.example.test/acme-logo.png";

    private static readonly object AcmeBranding = new
    {
        logoUrl = LogoUrl,
        primaryColor = "#0B5ED7",
        emailVerificationSubject = "Welcome to Acme Shop: confirm your email",
        emailVerificationIntro = "Thanks for joining Acme's loyalty programme. <script>alert(1)</script>",
        passwordResetSubject = "Reset your Acme password",
        passwordResetIntro = "Someone asked to reset your Acme password.",
    };

    [Fact]
    public async Task Branding_AppearsOnTheHostedLoginPage_OfEveryEnvironment_AndOnlyThere()
    {
        var scenario = await Scenario.CreateAsync(api);
        var other = await Scenario.CreateAsync(api);
        var saved = (await scenario.Owner.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.OK).Json;
        Assert.Equal("#0b5ed7", saved["primaryColor"]!.GetValue<string>());

        foreach (var environmentId in new[] { scenario.DevelopmentId, scenario.ProductionId })
        {
            var html = await LoginPageAsync(scenario, environmentId);
            Assert.Contains($"id=\"dp-tenant-logo\" class=\"dp-tenant-logo\" src=\"{LogoUrl}\"", html, StringComparison.Ordinal);
            Assert.Contains("--dp-orange: #0b5ed7;", html, StringComparison.Ordinal);
            Assert.Contains("--dp-on-orange: #ffffff;", html, StringComparison.Ordinal);
        }

        var otherHtml = await LoginPageAsync(other, other.DevelopmentId);
        Assert.DoesNotContain("dp-tenant-logo\"", otherHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("#0b5ed7", otherHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerificationEmail_UsesTheTenantTemplate_InTheBrandedLayout_WithEscapedText()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.OK);
        var email = $"brand-{Guid.NewGuid():N}@example.test";
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var userId = await KeycloakAdmin.Client.CreateUserAsync(realm, email, $"Pw-{Guid.NewGuid():N}", emailVerified: false, CancellationToken.None);

        await AdminPutAsync($"admin/realms/{realm}/users/{userId}/send-verify-email");
        var message = await Mailpit.WaitForMessageAsync(email, "Welcome to Acme Shop");

        Assert.Equal("Welcome to Acme Shop: confirm your email", message.Subject);
        Assert.Contains("Thanks for joining Acme&#39;s loyalty programme.", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", message.Html, StringComparison.Ordinal);
        Assert.Contains(LogoUrl, message.Html, StringComparison.Ordinal);
        Assert.Contains("border-top:5px solid #0b5ed7", message.Html, StringComparison.Ordinal);
        Assert.Contains("Secured by Dovepeak Identity", message.Html, StringComparison.Ordinal);
        Assert.Contains("/login-actions/action-token", message.Html, StringComparison.Ordinal);
        Assert.Contains("Thanks for joining Acme's loyalty programme.", message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnbrandedProject_UsesPlatformDefaults_AndResettingRemovesTheBranding()
    {
        var scenario = await Scenario.CreateAsync(api);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);

        (await scenario.Owner.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.OK);
        (await scenario.Owner.PutAsync($"{scenario.Project}/branding", new { })).Expect(HttpStatusCode.OK);

        Assert.DoesNotContain("dp-tenant-logo\"", await LoginPageAsync(scenario, scenario.DevelopmentId), StringComparison.Ordinal);
        var texts = await KeycloakAdmin.Client.GetRealmLocalizationAsync(realm, "en", CancellationToken.None);
        Assert.DoesNotContain(texts.Keys, k => TenantBranding.ManagedKeys.Contains(k));

        var email = $"plain-{Guid.NewGuid():N}@example.test";
        var userId = await KeycloakAdmin.Client.CreateUserAsync(realm, email, $"Pw-{Guid.NewGuid():N}", emailVerified: false, CancellationToken.None);
        await AdminPutAsync($"admin/realms/{realm}/users/{userId}/send-verify-email");
        var message = await Mailpit.WaitForMessageAsync(email, "Verify your email address");
        Assert.Contains("border-top:5px solid #ff6300", message.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("logoUrl", "http://cdn.example.test/logo.png")]
    [InlineData("logoUrl", "javascript:alert(1)")]
    [InlineData("primaryColor", "#0b5ed7; background: url(x)")]
    [InlineData("emailVerificationSubject", "Hi\r\nBcc: victim@example.test")]
    [InlineData("passwordResetIntro", "Hello {0}")]
    public async Task UnsafeBranding_IsRejected(string field, string value)
    {
        var scenario = await Scenario.CreateAsync(api);
        var body = new JsonObject { [field] = value };

        var response = (await scenario.Owner.PutAsync($"{scenario.Project}/branding", body)).Expect(HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", response.Code);
        Assert.Null((await scenario.Owner.GetAsync($"{scenario.Project}/branding")).Expect(HttpStatusCode.OK).Json[field]);
    }

    [Fact]
    public async Task ViewersCannotChangeBranding()
    {
        var scenario = await Scenario.CreateAsync(api);
        var key = (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "reader", scopes = new[] { "projects:read" } }))
            .Expect(HttpStatusCode.Created).Json["key"]!.GetValue<string>();
        using var reader = api.Client(key);

        (await reader.GetAsync($"{scenario.Project}/branding")).Expect(HttpStatusCode.OK);
        (await reader.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BrandingDrift_IsReverted()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.OK);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);

        // Someone replaces the logo with their own and edits the email text directly in Keycloak.
        await KeycloakAdmin.Client.SetRealmLocalizationTextAsync(realm, "en", "dovepeakBrandLogoUrl", "https://attacker.example/logo.png", CancellationToken.None);
        await KeycloakAdmin.Client.SetRealmLocalizationTextAsync(realm, "en", "dovepeakPasswordResetIntro", "Your account is suspended, sign in here.", CancellationToken.None);

        var corrections = await api.ReconcileAsync(scenario.DevelopmentId);

        Assert.Contains(corrections, c => c.Kind == "branding_restored");
        var texts = await KeycloakAdmin.Client.GetRealmLocalizationAsync(realm, "en", CancellationToken.None);
        Assert.Equal(LogoUrl, texts["dovepeakBrandLogoUrl"]);
        Assert.Equal("Someone asked to reset your Acme password.", texts["dovepeakPasswordResetIntro"]);
        Assert.Empty(await api.ReconcileAsync(scenario.DevelopmentId));
    }

    [Fact]
    public async Task PasswordChange_SendsABrandedSecurityAlert_ButFailedSignInsDoNot()
    {
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PutAsync($"{scenario.Project}/branding", AcmeBranding)).Expect(HttpStatusCode.OK);
        var created = await scenario.CreateApplicationAsync("spa");
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        var user = TestUser.Generate();
        await KeycloakAdmin.Client.CreateUserAsync(realm, user.Email, user.Password, emailVerified: true, CancellationToken.None);
        using var client = new OidcClient(Issuer(scenario.DevelopmentId), created["application"]!["clientId"]!.GetValue<string>(), null, TestRealm.RedirectUri);

        // A failed sign-in must not email the user (attackers could flood inboxes).
        using (var browser = new BrowserSession(TestRealm.RedirectUri))
        {
            var page = (await browser.GetAsync(client.CreateAuthorizationRequest().Url)).RequirePage();
            await browser.SubmitAsync(page, "kc-form-login", new Dictionary<string, string> { ["username"] = user.Email, ["password"] = "wrong-password" });
        }

        // The user resets their password through the hosted pages, using the tenant's reset email.
        using (var browser = new BrowserSession(TestRealm.RedirectUri))
        {
            var request = client.CreateAuthorizationRequest();
            var login = (await browser.GetAsync(request.Url)).RequirePage();
            var reset = (await browser.GetAsync(login.Link("/login-actions/reset-credentials"))).RequirePage();
            await browser.SubmitAsync(reset, "kc-reset-password-form", new Dictionary<string, string> { ["username"] = user.Email });

            var resetEmail = await Mailpit.WaitForMessageAsync(user.Email, "Reset your Acme password");
            Assert.Contains("Someone asked to reset your Acme password.", resetEmail.Html, StringComparison.Ordinal);
            var update = (await browser.GetAsync(resetEmail.ActionLink())).RequirePage();
            var password = $"New-{Guid.NewGuid():N}";
            await browser.SubmitAsync(update, "kc-passwd-update-form", new Dictionary<string, string> { ["password-new"] = password, ["password-confirm"] = password });
        }

        var alert = await Mailpit.WaitForMessageAsync(user.Email, "Security alert");
        // Keycloak 26 reports a password change as UPDATE_CREDENTIAL with the credential type "password".
        Assert.Equal("Security alert: your password or sign-in method changed", alert.Subject);
        Assert.Contains("password", alert.Html, StringComparison.Ordinal);
        Assert.Contains(LogoUrl, alert.Html, StringComparison.Ordinal);
        Assert.Contains("Secured by Dovepeak Identity", alert.Html, StringComparison.Ordinal);
        Assert.Equal(2, await Mailpit.CountMessagesAsync(user.Email)); // the reset email and the alert; nothing for the failed sign-in
    }

    private static Uri Issuer(Guid environmentId) =>
        new(StackSettings.Current.KeycloakUrl, $"realms/{RealmName.ForEnvironment(environmentId)}");

    private static async Task<string> LoginPageAsync(Scenario scenario, Guid environmentId)
    {
        var created = (await scenario.Owner.PostAsync(
            $"{scenario.Project}/environments/{environmentId}/applications",
            new { name = $"login-{Guid.NewGuid():N}"[..20], kind = "spa", redirectUris = new[] { TestRealm.RedirectUri.ToString() } }))
            .Expect(HttpStatusCode.Created).Json;
        using var client = new OidcClient(Issuer(environmentId), created["application"]!["clientId"]!.GetValue<string>(), null, TestRealm.RedirectUri);
        using var browser = new BrowserSession(TestRealm.RedirectUri);
        return (await browser.GetAsync(client.CreateAuthorizationRequest().Url)).RequirePage().Body;
    }

    private static async Task AdminPutAsync(string path)
    {
        var token = await KeycloakAdmin.TokenProvider.GetTokenAsync(CancellationToken.None);
        using var http = new HttpClient { BaseAddress = StackSettings.Current.KeycloakAdminUrl };
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
