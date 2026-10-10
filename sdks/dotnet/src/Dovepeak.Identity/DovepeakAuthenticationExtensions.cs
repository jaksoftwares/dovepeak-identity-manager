using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dovepeak.Identity;

/// <summary>One-line setup for APIs protected by Dovepeak Identity.</summary>
public static class DovepeakAuthenticationExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication for Dovepeak access tokens, plus authorization. Validates signature (RS256 only),
    /// issuer, audience and expiry; maps the flat <c>roles</c> claim to roles (so <c>RequireRole</c> and
    /// <c>User.IsInRole</c> work) and <c>sub</c> to the user name.
    /// </summary>
    /// <example><c>builder.Services.AddDovepeakAuthentication(builder.Configuration.GetSection("Dovepeak"));</c></example>
    public static IServiceCollection AddDovepeakAuthentication(this IServiceCollection services, IConfiguration configuration) =>
        services.AddDovepeakAuthentication(options => configuration.Bind(options));

    /// <inheritdoc cref="AddDovepeakAuthentication(IServiceCollection, IConfiguration)"/>
    public static IServiceCollection AddDovepeakAuthentication(this IServiceCollection services, Action<DovepeakAuthenticationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<DovepeakAuthenticationOptions>().Configure(configure).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DovepeakAuthenticationOptions>, DovepeakAuthenticationOptionsValidator>();
        services.AddHttpClient<TokenIntrospector>();
        services.AddMemoryCache();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<DovepeakAuthenticationOptions>>((jwt, dovepeak) => Configure(jwt, dovepeak.Value));

        services.AddAuthorization();
        return services;
    }

    private static void Configure(JwtBearerOptions jwt, DovepeakAuthenticationOptions options)
    {
        var issuer = options.Issuer.TrimEnd('/');
        jwt.Authority = issuer;
        jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,

            // Never accept "none" or symmetric algorithms (prevents algorithm-confusion attacks).
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            NameClaimType = DovepeakClaims.Subject,
            RoleClaimType = DovepeakClaims.Roles,
        };

        if (options.Introspection.Enabled)
        {
            jwt.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var introspector = context.HttpContext.RequestServices.GetRequiredService<TokenIntrospector>();
                    var token = context.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
                    if (!await introspector.IsActiveAsync(token, context.HttpContext.RequestAborted).ConfigureAwait(false))
                    {
                        context.Fail("The token has been revoked or its session has ended.");
                    }
                },
            };
        }
    }
}

/// <summary>Claim names in Dovepeak access tokens.</summary>
public static class DovepeakClaims
{
    /// <summary>The user (or, for machine tokens, the service account).</summary>
    public const string Subject = "sub";

    /// <summary>Application roles of the user, one claim per role.</summary>
    public const string Roles = "roles";

    /// <summary>Granted OAuth scopes, space-separated (RFC 8693).</summary>
    public const string Scope = "scope";

    /// <summary>The application the token was issued to.</summary>
    public const string ClientId = "azp";
}
