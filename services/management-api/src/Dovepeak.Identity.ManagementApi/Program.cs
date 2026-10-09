using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.ManagementApi.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformHealthChecks(builder.Configuration);
builder.Services.AddKeycloakAdmin(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapPlatformHealthChecks();

app.Run();
