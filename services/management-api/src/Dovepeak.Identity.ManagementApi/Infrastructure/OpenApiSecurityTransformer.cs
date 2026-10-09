using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Dovepeak.Identity.ManagementApi.Infrastructure;

/// <summary>Describes the API's authentication in the OpenAPI document.</summary>
internal sealed class OpenApiSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Dovepeak Identity Management API",
            Version = "v1",
            Description = "Manage organizations, projects, environments, applications, credentials, webhooks and audit events. " +
                "Authenticate with a developer access token from the platform realm, or a developer API key (dpk_…), " +
                "as an HTTP bearer token.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Developer access token (JWT) or developer API key (dpk_live_…).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] });

        return Task.CompletedTask;
    }
}
