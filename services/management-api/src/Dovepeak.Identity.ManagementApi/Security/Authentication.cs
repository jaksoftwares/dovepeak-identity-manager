using System.Security.Claims;
using System.Text.Encodings.Web;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Credentials;
using Dovepeak.Identity.Platform.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dovepeak.Identity.ManagementApi.Security;

internal static class AuthenticationSetup
{
    public const string PolicyScheme = "Dovepeak";
    public const string ApiKeyScheme = "ApiKey";

    /// <summary>
    /// Two credential types, never interchangeable (ADR-0004): developer access tokens from the platform realm, and
    /// developer API keys (<c>dpk_…</c>). A policy scheme routes each request to the right handler by the token's form.
    /// </summary>
    public static IServiceCollection AddManagementAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var platform = configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>()
            ?? throw new InvalidOperationException("The Platform section is not configured.");
        var keycloak = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
            ?? throw new InvalidOperationException("The Keycloak section is not configured.");

        var issuer = new Uri(platform.PublicIdentityUrl!, $"realms/{platform.PlatformRealm}").ToString();

        services.AddAuthentication(PolicyScheme)
            .AddPolicyScheme(PolicyScheme, "Developer token or API key", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var header = context.Request.Headers.Authorization.ToString();
                    return header.StartsWith("Bearer " + ApiKeyCodec.Prefix, StringComparison.Ordinal)
                        ? ApiKeyScheme
                        : JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer(options =>
            {
                // Keys are fetched over the internal network; the issuer is the public URL found in tokens.
                options.MetadataAddress = new Uri(keycloak.BaseUrl!, $"realms/{platform.PlatformRealm}/.well-known/openid-configuration").ToString();
                options.RequireHttpsMetadata = keycloak.BaseUrl!.Scheme == Uri.UriSchemeHttps;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = platform.ManagementApiAudience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, null);

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(PolicyScheme)
                .RequireAuthenticatedUser().Build();
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICallerAccessor, HttpCallerAccessor>();
        return services;
    }
}

/// <summary>Authenticates <c>Authorization: Bearer dpk_live_…</c> developer API keys against the database.</summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string OrganizationClaim = "dovepeak:org";
    public const string ScopeClaim = "scope";
    public const string KindClaim = "dovepeak:kind";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var validator = Context.RequestServices.GetRequiredService<ApiKeyValidator>();
        var key = await validator.ValidateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid, expired or revoked API key.");
        }

        var claims = new List<Claim>
        {
            new("sub", key.Id.ToString()),
            new(KindClaim, "api_key"),
            new(OrganizationClaim, key.OrganizationId.ToString()),
        };
        claims.AddRange(key.Scopes.Select(s => new Claim(ScopeClaim, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, "sub", null));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>Builds the platform <see cref="Caller"/> from the authenticated HTTP request.</summary>
internal sealed class HttpCallerAccessor(IHttpContextAccessor httpContextAccessor) : ICallerAccessor
{
    private Caller? _caller;

    public Caller Caller => _caller ??= Build();

    private Caller Build()
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            // Background work running inside the API process (for example tests driving processors).
            return Caller.System;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("No authenticated caller for this request.");
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (user.FindFirst(ApiKeyAuthenticationHandler.KindClaim)?.Value == "api_key")
        {
            return new Caller
            {
                Kind = CallerKind.ApiKey,
                Id = user.FindFirst("sub")!.Value,
                ApiKeyOrganizationId = Guid.Parse(user.FindFirst(ApiKeyAuthenticationHandler.OrganizationClaim)!.Value),
                ApiKeyPermissions = user.FindAll(ApiKeyAuthenticationHandler.ScopeClaim)
                    .Select(c => Permissions.TryParseScope(c.Value, out var p) ? (Permission?)p : null)
                    .OfType<Permission>().ToHashSet(),
                IpAddress = ip,
            };
        }

        return new Caller
        {
            Kind = CallerKind.Developer,
            Id = user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("Developer token has no subject."),
            Email = user.FindFirst("email")?.Value,
            EmailVerified = string.Equals(user.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            IpAddress = ip,
        };
    }
}
