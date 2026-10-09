using Dovepeak.Identity.ManagementApi.Infrastructure;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Applications;
using Dovepeak.Identity.Platform.Projects;

namespace Dovepeak.Identity.ManagementApi.Endpoints;

internal static class ProjectEndpoints
{
    public sealed record CreateProjectRequest(string Slug, string Name);

    public sealed record RenameProjectRequest(string Name);

    public sealed record CreateApplicationRequest(
        string Name,
        ApplicationKind Kind,
        IReadOnlyList<string>? RedirectUris,
        IReadOnlyList<string>? PostLogoutRedirectUris,
        IReadOnlyList<string>? WebOrigins,
        IReadOnlyList<string>? Audiences,
        ApplicationTokenPolicy? TokenPolicy,
        IReadOnlyList<string>? Scopes);

    public sealed record CreateScopeRequest(string Name, string? Description);

    public sealed record CreateRoleRequest(string Name, string? Description);

    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder v1)
    {
        var org = OrganizationEndpoints.OrganizationGroup(v1);

        org.MapGet("/projects", (Guid orgId, ProjectService s, CancellationToken ct) => s.ListAsync(orgId, ct)).WithTags("Projects");
        org.MapPost("/projects", async (Guid orgId, CreateProjectRequest request, ProjectService s, CancellationToken ct) =>
            {
                var project = await s.CreateAsync(orgId, request.Slug, request.Name, ct);
                return Results.Created($"/v1/organizations/{orgId}/projects/{project.Id}", project);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithTags("Projects");
        org.MapGet("/projects/{projectId:guid}", (Guid orgId, Guid projectId, ProjectService s, CancellationToken ct) =>
            s.GetAsync(orgId, projectId, ct)).WithTags("Projects");
        org.MapPatch("/projects/{projectId:guid}", (Guid orgId, Guid projectId, RenameProjectRequest request, ProjectService s, CancellationToken ct) =>
            s.RenameAsync(orgId, projectId, request.Name, ct)).WithTags("Projects");
        org.MapDelete("/projects/{projectId:guid}", async (Guid orgId, Guid projectId, ProjectService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(orgId, projectId, ct);
            return Results.NoContent();
        }).WithTags("Projects");

        var env = org.MapGroup("/projects/{projectId:guid}/environments/{environmentId:guid}");
        env.MapGet("", (Guid orgId, Guid projectId, Guid environmentId, ProjectService s, CancellationToken ct) =>
            s.GetEnvironmentAsync(orgId, projectId, environmentId, ct)).WithTags("Environments");

        // OAuth scopes
        env.MapGet("/scopes", (Guid orgId, Guid projectId, Guid environmentId, ScopeService s, CancellationToken ct) =>
            s.ListAsync(orgId, projectId, environmentId, ct)).WithTags("Scopes");
        env.MapPost("/scopes", async (Guid orgId, Guid projectId, Guid environmentId, CreateScopeRequest request, ScopeService s, CancellationToken ct) =>
            {
                var scope = await s.CreateAsync(orgId, projectId, environmentId, request.Name, request.Description, ct);
                return Results.Created($"/v1/organizations/{orgId}/projects/{projectId}/environments/{environmentId}/scopes/{scope.Name}", scope);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithTags("Scopes");
        env.MapDelete("/scopes/{scopeName}", async (Guid orgId, Guid projectId, Guid environmentId, string scopeName, ScopeService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(orgId, projectId, environmentId, scopeName, ct);
            return Results.NoContent();
        }).WithTags("Scopes");

        // Applications
        env.MapGet("/applications", (Guid orgId, Guid projectId, Guid environmentId, ApplicationService s, CancellationToken ct) =>
            s.ListAsync(orgId, projectId, environmentId, ct)).WithTags("Applications");
        env.MapPost("/applications", async (Guid orgId, Guid projectId, Guid environmentId, CreateApplicationRequest request, ApplicationService s,
                HttpContext http, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(orgId, projectId, environmentId, new ApplicationSettings(
                    request.Name, request.Kind, request.RedirectUris, request.PostLogoutRedirectUris, request.WebOrigins, request.Audiences, request.TokenPolicy, request.Scopes), ct);
                NoStore(http);
                return Results.Created($"/v1/organizations/{orgId}/projects/{projectId}/environments/{environmentId}/applications/{created.Application.Id}", created);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithMetadata(new SecretBearingResponseAttribute()).WithTags("Applications");

        var app = env.MapGroup("/applications/{applicationId:guid}").WithTags("Applications");
        app.MapGet("", (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationService s, CancellationToken ct) =>
            s.GetAsync(orgId, projectId, environmentId, applicationId, ct));
        app.MapPatch("", (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationUpdate update, ApplicationService s, CancellationToken ct) =>
            s.UpdateAsync(orgId, projectId, environmentId, applicationId, update, ct));
        app.MapDelete("", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(orgId, projectId, environmentId, applicationId, ct);
            return Results.NoContent();
        });
        app.MapGet("/config", (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationService s, CancellationToken ct) =>
            s.GetConfigAsync(orgId, projectId, environmentId, applicationId, ct));
        app.MapPost("/secret", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, bool? revokePrevious, ApplicationService s, HttpContext http, CancellationToken ct) =>
            {
                var rotated = await s.RotateSecretAsync(orgId, projectId, environmentId, applicationId, revokePrevious ?? false, ct);
                NoStore(http);
                return Results.Ok(rotated);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithMetadata(new SecretBearingResponseAttribute());
        app.MapDelete("/secret/previous", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationService s, CancellationToken ct) =>
        {
            await s.RevokePreviousSecretAsync(orgId, projectId, environmentId, applicationId, ct);
            return Results.NoContent();
        });

        // Roles and assignments
        app.MapGet("/roles", (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, RoleService s, CancellationToken ct) =>
            s.ListRolesAsync(orgId, projectId, environmentId, applicationId, ct)).WithTags("Roles");
        app.MapPost("/roles", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, CreateRoleRequest request, RoleService s, CancellationToken ct) =>
            {
                var role = await s.CreateRoleAsync(orgId, projectId, environmentId, applicationId, request.Name, request.Description, ct);
                return Results.Created($"/v1/organizations/{orgId}/projects/{projectId}/environments/{environmentId}/applications/{applicationId}/roles/{role.Name}", role);
            })
            .AddEndpointFilter<IdempotencyFilter>().WithTags("Roles");
        app.MapDelete("/roles/{roleName}", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string roleName, RoleService s, CancellationToken ct) =>
        {
            await s.DeleteRoleAsync(orgId, projectId, environmentId, applicationId, roleName, ct);
            return Results.NoContent();
        }).WithTags("Roles");
        app.MapPut("/roles/{roleName}/users/{userId}", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string roleName, string userId,
            RoleService s, CancellationToken ct) =>
        {
            await s.SetAssignmentAsync(orgId, projectId, environmentId, applicationId, roleName, userId, assigned: true, ct);
            return Results.NoContent();
        }).WithTags("Roles");
        app.MapDelete("/roles/{roleName}/users/{userId}", async (Guid orgId, Guid projectId, Guid environmentId, Guid applicationId, string roleName, string userId,
            RoleService s, CancellationToken ct) =>
        {
            await s.SetAssignmentAsync(orgId, projectId, environmentId, applicationId, roleName, userId, assigned: false, ct);
            return Results.NoContent();
        }).WithTags("Roles");

        // End users and sessions
        var users = env.MapGroup("/users").WithTags("Users");
        users.MapGet("", (Guid orgId, Guid projectId, Guid environmentId, string? email, int? first, int? max, RoleService s, CancellationToken ct) =>
            s.SearchUsersAsync(orgId, projectId, environmentId, email, first ?? 0, max ?? 50, ct));
        users.MapGet("/{userId}/sessions", (Guid orgId, Guid projectId, Guid environmentId, string userId, RoleService s, CancellationToken ct) =>
            s.ListSessionsAsync(orgId, projectId, environmentId, userId, ct));
        users.MapDelete("/{userId}/sessions", async (Guid orgId, Guid projectId, Guid environmentId, string userId, RoleService s, CancellationToken ct) =>
        {
            await s.RevokeSessionsAsync(orgId, projectId, environmentId, userId, sessionId: null, ct);
            return Results.NoContent();
        });
        users.MapDelete("/{userId}/sessions/{sessionId}", async (Guid orgId, Guid projectId, Guid environmentId, string userId, string sessionId,
            RoleService s, CancellationToken ct) =>
        {
            await s.RevokeSessionsAsync(orgId, projectId, environmentId, userId, sessionId, ct);
            return Results.NoContent();
        });

        return v1;
    }

    /// <summary>Responses carrying one-time secrets must never be cached by browsers or proxies.</summary>
    internal static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";
}
