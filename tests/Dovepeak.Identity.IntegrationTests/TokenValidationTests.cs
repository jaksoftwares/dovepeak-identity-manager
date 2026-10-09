extern alias ProtectedApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// Milestone M1.3 — the example resource server accepts valid access tokens and rejects every forged,
/// mis-addressed or expired token (threat model S-02).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TokenValidationTests(TokenValidationTests.Fixture fixture) : IClassFixture<TokenValidationTests.Fixture>
{
    [Fact]
    public async Task ValidToken_IsAccepted()
    {
        var (user, tokens) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);

        using var response = await CallMeAsync(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(user.Id!, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingToken_IsRejected()
    {
        using var response = await CallMeAsync(token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnsignedToken_AlgNone_IsRejected()
    {
        var (_, tokens) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);
        var parts = tokens.AccessToken.Split('.');
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");

        using var response = await CallMeAsync($"{header}.{parts[1]}.");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedPayload_IsRejected()
    {
        var (_, tokens) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);
        var parts = tokens.AccessToken.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"sub\":\"", "\"sub\":\"attacker-", StringComparison.Ordinal);

        using var response = await CallMeAsync($"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SymmetricAlgorithmConfusion_IsRejected()
    {
        // Attack: sign with HS256 using the server's public key as the HMAC secret.
        var (_, tokens) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.WebApp);
        var original = new JwtSecurityToken(tokens.AccessToken);
        var key = new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Realm.Issuer.ToString())));
        var forged = new JwtSecurityToken(
            issuer: original.Issuer,
            audience: TestRealm.ApiAudience,
            claims: original.Claims.Where(c => c.Type is "sub" or "azp"),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        using var response = await CallMeAsync(new JwtSecurityTokenHandler().WriteToken(forged));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutApiAudience_IsRejected()
    {
        var (_, tokens) = await SignIn.NewUserAsync(fixture.Realm, fixture.Realm.NoAudienceApp);

        using var response = await CallMeAsync(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenFromAnotherTenant_IsRejected()
    {
        // Threat model I-01: a token issued by tenant B must never be accepted by tenant A's API.
        var (_, tokens) = await SignIn.NewUserAsync(fixture.OtherTenant, fixture.OtherTenant.WebApp);

        using var response = await CallMeAsync(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        // The short-lived realm issues 5-second access tokens; the API is configured with zero clock skew.
        var (_, tokens) = await SignIn.NewUserAsync(fixture.ShortLived, fixture.ShortLived.WebApp);
        using var shortLivedApi = fixture.CreateApiFor(fixture.ShortLived);

        using var before = await CallMeAsync(tokens.AccessToken, shortLivedApi);
        await Task.Delay(TimeSpan.FromSeconds(7));
        using var after = await CallMeAsync(tokens.AccessToken, shortLivedApi);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private Task<HttpResponseMessage> CallMeAsync(string? token, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return (client ?? fixture.Api).SendAsync(request);
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly List<IDisposable> _disposables = [];

        public TestRealm Realm { get; } = new();

        public TestRealm OtherTenant { get; } = new();

        public ShortLivedRealm ShortLived { get; } = new();

        public HttpClient Api { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            await Task.WhenAll(Realm.InitializeAsync(), OtherTenant.InitializeAsync(), ShortLived.InitializeAsync());
            Api = CreateApiFor(Realm);
        }

        public HttpClient CreateApiFor(TestRealm realm)
        {
            var factory = new WebApplicationFactory<ProtectedApi::Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Auth:Issuer", realm.Issuer.ToString());
                builder.UseSetting("Auth:Audience", TestRealm.ApiAudience);
                builder.UseSetting("Auth:RequireHttpsMetadata", "false");
                builder.UseSetting("Auth:ClockSkewSeconds", "0");
            });
            _disposables.Add(factory);
            return factory.CreateClient();
        }

        public async Task DisposeAsync()
        {
            _disposables.ForEach(d => d.Dispose());
            await Task.WhenAll(Realm.DisposeAsync(), OtherTenant.DisposeAsync(), ShortLived.DisposeAsync());
        }
    }
}

/// <summary>A realm with very short token and session lifetimes, for expiry tests.</summary>
public sealed class ShortLivedRealm : TestRealm
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Admin.UpdateRealmAsync(Name, new System.Text.Json.Nodes.JsonObject
        {
            ["accessTokenLifespan"] = 5,
            ["ssoSessionMaxLifespan"] = 15,
        }, CancellationToken.None);
    }
}
