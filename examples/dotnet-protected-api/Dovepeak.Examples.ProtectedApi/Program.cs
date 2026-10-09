using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// Example resource server protected by Dovepeak Identity access tokens.
// Demonstrates the validation every backend must perform (problem statement §4.4, ADR-0003):
// signature, issuer, audience, expiry and an explicit algorithm allow-list.

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth");
var issuer = auth["Issuer"] ?? throw new InvalidOperationException("Auth:Issuer is not configured.");
var audience = auth["Audience"] ?? throw new InvalidOperationException("Auth:Audience is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Signing keys are fetched from the issuer's JWKS endpoint via OIDC discovery.
        options.Authority = issuer;
        options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", defaultValue: true);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,

            // Never accept "none" or symmetric algorithms (prevents algorithm-confusion attacks).
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

            ClockSkew = TimeSpan.FromSeconds(auth.GetValue("ClockSkewSeconds", defaultValue: 30)),
            NameClaimType = "sub",
        };
    });

// Application roles managed in Dovepeak Identity arrive as a flat "roles" claim. ("admin" itself is reserved by the
// identity engine, so the example uses "administrator".)
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("admin", policy => policy.RequireClaim("roles", "administrator"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/public", () => Results.Ok(new { message = "Anyone can read this." }));

app.MapGet("/me", (HttpContext context) => Results.Ok(new
{
    subject = context.User.FindFirst("sub")?.Value,
    email = context.User.FindFirst("email")?.Value,
    clientId = context.User.FindFirst("azp")?.Value,
})).RequireAuthorization();

app.MapGet("/admin", () => Results.Ok(new { message = "Only users with the administrator role can read this." }))
    .RequireAuthorization("admin");

app.Run();
