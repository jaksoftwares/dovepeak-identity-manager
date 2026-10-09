using System.Text.Json;
using System.Text.Json.Serialization;
using Dovepeak.Identity.ManagementApi.Endpoints;
using Dovepeak.Identity.ManagementApi.Health;
using Dovepeak.Identity.ManagementApi.Infrastructure;
using Dovepeak.Identity.ManagementApi.Security;
using Dovepeak.Identity.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformHealthChecks(builder.Configuration);
builder.Services.AddManagementAuthentication(builder.Configuration);
builder.Services.AddPlatform(builder.Configuration);
builder.Services.AddHostedService<PlatformRealmStartup>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer<OpenApiSecurityTransformer>());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapPlatformHealthChecks();
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

var v1 = app.MapGroup("/v1");
v1.MapOrganizationEndpoints();
v1.MapProjectEndpoints();
v1.MapPlatformEndpoints();

app.Run();
