using System.Net;
using System.Security.Claims;
using System.Text;
using Dovepeak.Identity.Management;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Tests;

public sealed class AuthenticationOptionsTests
{
    private static ServiceProvider Build(Action<DovepeakAuthenticationOptions> configure) =>
        new ServiceCollection().AddLogging().AddDovepeakAuthentication(configure).BuildServiceProvider();

    [Fact]
    public void ValidSettings_ConfigureStrictJwtValidation()
    {
        var services = Build(o =>
        {
            o.Issuer = "https://id.example.test/realms/dp-1/";
            o.Audience = "orders-api";
        });

        var jwt = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("https://id.example.test/realms/dp-1", jwt.Authority);
        Assert.Equal("https://id.example.test/realms/dp-1", jwt.TokenValidationParameters.ValidIssuer);
        Assert.Equal("orders-api", jwt.TokenValidationParameters.ValidAudience);
        Assert.Equal(["RS256"], jwt.TokenValidationParameters.ValidAlgorithms);
        Assert.Equal("roles", jwt.TokenValidationParameters.RoleClaimType);
        Assert.False(jwt.MapInboundClaims);
    }

    [Theory]
    [InlineData("", "orders-api", "Issuer must be an absolute URL")]
    [InlineData("http://id.example.test/realms/x", "orders-api", "HTTPS")]
    [InlineData("https://id.example.test/realms/x", "", "Audience is required")]
    public void UnsafeSettings_FailAtStartup(string issuer, string audience, string message)
    {
        var services = Build(o =>
        {
            o.Issuer = issuer;
            o.Audience = audience;
        });

        var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<DovepeakAuthenticationOptions>>().Value);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Introspection_RequiresCredentials()
    {
        var services = Build(o =>
        {
            o.Issuer = "https://id.example.test/realms/x";
            o.Audience = "a";
            o.Introspection.Enabled = true;
        });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<DovepeakAuthenticationOptions>>().Value);
    }
}

public sealed class AuthorizationTests
{
    private static ClaimsPrincipal User(string? scope = null, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", "user-1") };
        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "sub", "roles"));
    }

    private static async Task<bool> AuthorizeAsync(ClaimsPrincipal user, Action<AuthorizationPolicyBuilder> policy)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var builder = new AuthorizationPolicyBuilder();
        policy(builder);
        return (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, builder.Build())).Succeeded;
    }

    [Fact]
    public async Task RequireScope_ChecksTheSpaceSeparatedScopeClaim()
    {
        Assert.True(await AuthorizeAsync(User("openid orders:read"), p => p.RequireScope("orders:read")));
        Assert.False(await AuthorizeAsync(User("openid orders:read"), p => p.RequireScope("orders:read", "orders:write")));
        Assert.False(await AuthorizeAsync(User("openid orders:readonly"), p => p.RequireScope("orders:read")));
        Assert.False(await AuthorizeAsync(User(), p => p.RequireScope("orders:read")));
    }

    [Fact]
    public async Task Roles_WorkWithDovepeakAndStandardRoleChecks()
    {
        var admin = User(null, "administrator");
        Assert.True(await AuthorizeAsync(admin, p => p.RequireDovepeakRole("administrator")));
        Assert.True(await AuthorizeAsync(admin, p => p.RequireRole("administrator")));
        Assert.True(admin.IsInRole("administrator"));
        Assert.False(await AuthorizeAsync(User(null, "viewer"), p => p.RequireDovepeakRole("administrator")));
    }
}

public sealed class ManagementClientTests
{
    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static (DovepeakManagementClient Client, Recorder Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Recorder(respond);
        var client = new DovepeakManagementClient(
            new HttpClient(handler),
            Options.Create(new DovepeakManagementOptions { BaseUrl = new Uri("https://api.example.test/"), ApiKey = "dpk_live_test" }));
        return (client, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Requests_AreAuthenticated_AndCreatesAreIdempotent()
    {
        var org = Guid.NewGuid();
        var (client, handler) = Create(_ => Json(HttpStatusCode.Created,
            """{"id":"7b8e9c40-0000-0000-0000-000000000001","slug":"shop","name":"Shop","createdAt":"2026-10-10T00:00:00Z","environments":[]}"""));

        var project = await client.CreateProjectAsync(org, "shop", "Shop");

        Assert.Equal("shop", project.Slug);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://api.example.test/v1/organizations/{org}/projects", request.RequestUri!.ToString());
        Assert.Equal("Bearer dpk_live_test", request.Headers.Authorization!.ToString());
        Assert.True(Guid.TryParse(request.Headers.GetValues("Idempotency-Key").Single(), out _));
    }

    [Fact]
    public async Task Problems_BecomeTypedExceptions()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Conflict,
            """{"title":"Quota exceeded","detail":"You can have at most 10 projects.","status":409,"code":"quota_exceeded"}"""));

        var error = await Assert.ThrowsAsync<DovepeakApiException>(() => client.CreateProjectAsync(Guid.NewGuid(), "shop", "Shop"));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("quota_exceeded", error.Code);
        Assert.Equal("You can have at most 10 projects.", error.Message);
    }

    [Fact]
    public async Task ValidationErrors_AreExposedByField()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"The request is invalid.","status":400,"code":"validation_failed","errors":{"slug":["Use lowercase letters."]}}"""));

        var error = await Assert.ThrowsAsync<DovepeakApiException>(() => client.CreateProjectAsync(Guid.NewGuid(), "Shop!", "Shop"));

        Assert.Equal(["Use lowercase letters."], error.Errors["slug"]);
    }

    [Fact]
    public void MissingSettings_AreRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            new DovepeakManagementClient(new HttpClient(), Options.Create(new DovepeakManagementOptions())));
}
