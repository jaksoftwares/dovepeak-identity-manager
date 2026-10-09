using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.ManagementApi.Tests.Keycloak;

public sealed class TokenPolicyTests
{
    [Fact]
    public void RealmBaselineConstants_MatchTheRealmTemplate()
    {
        var baseline = RealmTemplate.LoadEmbedded().Baseline();

        Assert.Equal(TokenPolicy.RealmAccessTokenLifetimeSeconds, baseline["accessTokenLifespan"]!.GetValue<int>());
        Assert.Equal(TokenPolicy.RealmSessionIdleTimeoutSeconds, baseline["ssoSessionIdleTimeout"]!.GetValue<int>());
        Assert.Equal(TokenPolicy.RealmSessionMaxLifetimeSeconds, baseline["ssoSessionMaxLifespan"]!.GetValue<int>());
    }

    [Fact]
    public void InheritedPolicy_UsesTheRealmBaseline_AndEmptyEngineAttributes()
    {
        var policy = TokenPolicy.Inherit;
        policy.Validate(ClientKind.Public);

        Assert.Equal(600, policy.EffectiveAccessTokenLifetimeSeconds);
        Assert.Equal(1800, policy.EffectiveSessionIdleTimeoutSeconds);
        Assert.Equal(43200, policy.EffectiveSessionMaxLifetimeSeconds);
        Assert.All(policy.ToAttributes(), a => Assert.Equal(string.Empty, a.Value));
    }

    [Fact]
    public void ConfiguredPolicy_IsWrittenToTheClientRepresentation()
    {
        var registration = new ClientRegistration("app", ClientKind.Public)
        {
            RedirectUris = [new Uri("https://app.example.com/callback")],
            TokenPolicy = new TokenPolicy(300, 900, 3600),
        };

        var attributes = ClientRepresentation.Build(registration)["attributes"]!;

        Assert.Equal("300", attributes["access.token.lifespan"]!.GetValue<string>());
        Assert.Equal("900", attributes["client.session.idle.timeout"]!.GetValue<string>());
        Assert.Equal("3600", attributes["client.session.max.lifespan"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(299, null, null)]
    [InlineData(3601, null, null)]
    [InlineData(null, 299, null)]
    [InlineData(null, 1801, null)]
    [InlineData(null, null, 43201)]
    [InlineData(null, 1200, 600)]
    public void OutOfRangeOrInconsistentValues_AreRejected(int? accessToken, int? idle, int? max) =>
        Assert.Throws<ArgumentException>(() => new TokenPolicy(accessToken, idle, max).Validate(ClientKind.Public));

    [Fact]
    public void ShortMaximumWithInheritedIdleTimeout_IsAllowed() =>
        new TokenPolicy(SessionMaxLifetimeSeconds: 900).Validate(ClientKind.Public);

    [Fact]
    public void MachineClients_AcceptTokenLifetime_ButNotSessionTimeouts()
    {
        new TokenPolicy(AccessTokenLifetimeSeconds: 300).Validate(ClientKind.Machine);
        Assert.Throws<ArgumentException>(() => new TokenPolicy(SessionIdleTimeoutSeconds: 600).Validate(ClientKind.Machine));
    }
}
