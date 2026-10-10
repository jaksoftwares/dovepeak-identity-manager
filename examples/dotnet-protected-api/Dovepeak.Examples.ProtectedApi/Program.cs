using Dovepeak.Identity;

// Example resource server protected by Dovepeak Identity access tokens, using only the Dovepeak.Identity SDK.
// The SDK validates signature (RS256 only), issuer, audience and expiry (problem statement §4.4, ADR-0003), and
// maps the token's roles and scopes for authorization.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDovepeakAuthentication(builder.Configuration.GetSection("Auth"));

// Browser apps calling this API directly (the React SPA example) need CORS; a BFF does not.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).WithHeaders("Authorization").WithMethods("GET")));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/public", () => Results.Ok(new { message = "Anyone can read this." }));

app.MapGet("/me", (HttpContext context) => Results.Ok(new
{
    subject = context.User.FindFirst(DovepeakClaims.Subject)?.Value,
    email = context.User.FindFirst("email")?.Value,
    clientId = context.User.FindFirst(DovepeakClaims.ClientId)?.Value,
    roles = context.User.FindAll(DovepeakClaims.Roles).Select(c => c.Value),
    scopes = context.User.GetScopes(),
})).RequireAuthorization();

// Application roles managed in Dovepeak Identity. ("admin" itself is reserved by the identity engine.)
app.MapGet("/admin", () => Results.Ok(new { message = "Only users with the administrator role can read this." }))
    .RequireDovepeakRole("administrator");

// OAuth scopes granted to the calling application and requested in the token.
app.MapGet("/orders", () => Results.Ok(new { message = "Only tokens granted the orders:read scope can read this." }))
    .RequireScope("orders:read");

app.Run();
