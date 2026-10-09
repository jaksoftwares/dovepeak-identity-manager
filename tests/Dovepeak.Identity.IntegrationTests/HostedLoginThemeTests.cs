using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>Milestone M1.5 — the hosted login pages use the Dovepeak theme and show each tenant's own name.</summary>
[Trait("Category", "Integration")]
public sealed class HostedLoginThemeTests
{
    [Fact]
    public async Task LoginPage_UsesDovepeakTheme_AndShowsTenantName()
    {
        var page = await LoginPageForNewRealmAsync("Acme Payments");

        Assert.Contains("/dovepeak/css/dovepeak.css", page.Body, StringComparison.Ordinal);
        Assert.Contains("Acme Payments", page.Body, StringComparison.Ordinal);
        Assert.True(page.HasForm("kc-form-login"));
    }

    [Fact]
    public async Task TwoTenants_ShareTheTheme_ButShowTheirOwnNames()
    {
        var first = await LoginPageForNewRealmAsync("Northwind Health");
        var second = await LoginPageForNewRealmAsync("Contoso Logistics");

        Assert.Contains("Northwind Health", first.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Contoso Logistics", first.Body, StringComparison.Ordinal);
        Assert.Contains("Contoso Logistics", second.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantName_IsHtmlEncoded()
    {
        // Display names come from tenants; they must never inject markup into the hosted login page.
        var page = await LoginPageForNewRealmAsync("<script>alert('x')</script> Evil Corp");

        Assert.DoesNotContain("<script>alert('x')</script>", page.Body, StringComparison.Ordinal);
        Assert.Contains("Evil Corp", page.Body, StringComparison.Ordinal);
    }

    private static async Task<HtmlPage> LoginPageForNewRealmAsync(string displayName)
    {
        var realm = RealmName.Parse($"it-{Guid.NewGuid():N}");
        await TestRealm.Admin.CreateRealmAsync(realm, displayName, CancellationToken.None);
        try
        {
            await TestRealm.Admin.CreateClientAsync(realm, new ClientRegistration("web-app", ClientKind.Public)
            {
                RedirectUris = [TestRealm.RedirectUri],
            }, CancellationToken.None);

            var issuer = new Uri(StackSettings.Current.KeycloakUrl, $"realms/{realm}");
            using var client = new OidcClient(issuer, "web-app", null, TestRealm.RedirectUri);
            using var browser = new BrowserSession(TestRealm.RedirectUri);
            return (await browser.GetAsync(client.CreateAuthorizationRequest().Url)).RequirePage();
        }
        finally
        {
            await TestRealm.Admin.DeleteRealmAsync(realm, CancellationToken.None);
        }
    }
}
