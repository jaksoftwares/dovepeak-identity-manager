using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.ManagementApi.Tests.Keycloak;

public sealed class TenantBrandingTests
{
    [Fact]
    public void DefaultBranding_RemovesEveryOverride()
    {
        TenantBranding.Default.Validate();
        var texts = TenantBranding.Default.ToLocalizationTexts();

        Assert.Equal(TenantBranding.ManagedKeys.Order(), texts.Keys.Order());
        Assert.All(texts.Values, Assert.Null);
    }

    [Fact]
    public void Colours_AreNormalised_WithHoverShadeAndReadableText()
    {
        var texts = new TenantBranding(PrimaryColor: "#0B5ED7").ToLocalizationTexts();

        Assert.Equal("#0b5ed7", texts["dovepeakBrandPrimaryColor"]);
        Assert.Matches("^#[0-9a-f]{6}$", texts["dovepeakBrandPrimaryColorDark"]!);
        Assert.Equal("#ffffff", texts["dovepeakBrandOnPrimaryColor"]);
    }

    [Theory]
    [InlineData("#ff6300", "#000027")] // Dovepeak orange: navy text contrasts more than white.
    [InlineData("#000027", "#ffffff")]
    [InlineData("#ffd400", "#000027")]
    [InlineData("#0b5ed7", "#ffffff")]
    public void ButtonText_UsesTheMoreReadableColour(string primary, string expected) =>
        Assert.Equal(expected, TenantBranding.TextColorOn(primary));

    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@cdn.example.com/logo.png")]
    [InlineData("https://cdn.example.com/logo.png\" onerror=\"alert(1)")]
    [InlineData("/relative/logo.png")]
    public void UnsafeLogoUrls_AreRejected(string url) =>
        Assert.Throws<ArgumentException>(() => new TenantBranding(LogoUrl: url).Validate());

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#12345g")]
    [InlineData("#123456; background:url(x)")]
    public void InvalidColours_AreRejected(string colour) =>
        Assert.Throws<ArgumentException>(() => new TenantBranding(PrimaryColor: colour).Validate());

    [Fact]
    public void Subjects_CannotSpanLines_PreventingHeaderInjection()
    {
        Assert.Throws<ArgumentException>(() => new TenantBranding(EmailVerificationSubject: "Hello\r\nBcc: victim@example.com").Validate());
        new TenantBranding(EmailVerificationIntro: "Line one\nLine two").Validate();
    }

    [Fact]
    public void Braces_AreRejected_AndQuotesEscapedForMessageFormat()
    {
        Assert.Throws<ArgumentException>(() => new TenantBranding(PasswordResetIntro: "Hi {0}").Validate());

        var texts = new TenantBranding(PasswordResetSubject: "Reset your Acme's password").ToLocalizationTexts();
        Assert.Equal("Reset your Acme''s password", texts["passwordResetSubject"]);
    }

    [Fact]
    public void OverlongTexts_AreRejected() =>
        Assert.Throws<ArgumentException>(() => new TenantBranding(EmailVerificationIntro: new string('a', TenantBranding.MaxIntroLength + 1)).Validate());
}
