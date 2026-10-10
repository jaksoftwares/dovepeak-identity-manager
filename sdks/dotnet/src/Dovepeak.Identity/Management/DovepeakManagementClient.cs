using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Management;

/// <summary>Connection settings for the Management API.</summary>
public sealed class DovepeakManagementOptions
{
    /// <summary>Base URL of the Management API, e.g. https://api.identity.example.com/.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>An organization API key (<c>dpk_live_…</c>). Keep it in a secret store.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Typed client for the Dovepeak Management API, for automation and infrastructure-as-code. Authenticates with an
/// organization API key; create requests carry an <c>Idempotency-Key</c> so retries never create duplicates.
/// Register it with <see cref="ManagementServiceCollectionExtensions.AddDovepeakManagementClient(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{DovepeakManagementOptions})"/>.
/// </summary>
public sealed class DovepeakManagementClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    /// <summary>Creates a client over an <see cref="HttpClient"/> (normally supplied by <c>IHttpClientFactory</c>).</summary>
    public DovepeakManagementClient(HttpClient http, IOptions<DovepeakManagementOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        if (settings.BaseUrl is null || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Dovepeak management client: BaseUrl and ApiKey are required.");
        }

        _http = http;
        _http.BaseAddress ??= settings.BaseUrl;
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    // ---------------------------------------------------------------- Projects and environments

    /// <summary>Lists the organization's projects.</summary>
    public Task<IReadOnlyList<Project>> ListProjectsAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<Project>>(HttpMethod.Get, Org(organizationId, "/projects"), null, false, cancellationToken);

    /// <summary>Creates a project; its three environments are provisioned asynchronously (see <see cref="WaitUntilReadyAsync"/>).</summary>
    public Task<Project> CreateProjectAsync(Guid organizationId, string slug, string name, CancellationToken cancellationToken = default) =>
        SendAsync<Project>(HttpMethod.Post, Org(organizationId, "/projects"), new { slug, name }, true, cancellationToken);

    /// <summary>Gets a project with the state of its environments.</summary>
    public Task<Project> GetProjectAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default) =>
        SendAsync<Project>(HttpMethod.Get, Org(organizationId, $"/projects/{projectId}"), null, false, cancellationToken);

    /// <summary>Deletes a project and its environments (delete its applications first).</summary>
    public Task DeleteProjectAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Org(organizationId, $"/projects/{projectId}"), cancellationToken);

    /// <summary>Polls until every environment of the project is ready (provisioning usually takes seconds).</summary>
    public async Task<Project> WaitUntilReadyAsync(Guid organizationId, Guid projectId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        while (true)
        {
            var project = await GetProjectAsync(organizationId, projectId, cancellationToken).ConfigureAwait(false);
            if (project.Environments.All(e => e.IsReady))
            {
                return project;
            }

            if (project.Environments.Any(e => e.State == "failed") || DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Project {projectId} environments are not ready: {string.Join(", ", project.Environments.Select(e => $"{e.Kind}={e.State}"))}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- Applications

    /// <summary>Lists an environment's applications.</summary>
    public Task<IReadOnlyList<Application>> ListApplicationsAsync(Guid organizationId, Guid projectId, Guid environmentId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<Application>>(HttpMethod.Get, Env(organizationId, projectId, environmentId, "/applications"), null, false, cancellationToken);

    /// <summary>Registers an application. The client secret of web and machine apps is returned only here.</summary>
    public Task<CreatedApplication> CreateApplicationAsync(Guid organizationId, Guid projectId, Guid environmentId, NewApplication application, CancellationToken cancellationToken = default) =>
        SendAsync<CreatedApplication>(HttpMethod.Post, Env(organizationId, projectId, environmentId, "/applications"), application, true, cancellationToken);

    /// <summary>Gets an application.</summary>
    public Task<Application> GetApplicationAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken cancellationToken = default) =>
        SendAsync<Application>(HttpMethod.Get, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}"), null, false, cancellationToken);

    /// <summary>Changes an application; the identity engine is updated before this returns.</summary>
    public Task<Application> UpdateApplicationAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationChanges changes, CancellationToken cancellationToken = default) =>
        SendAsync<Application>(HttpMethod.Patch, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}"), changes, false, cancellationToken);

    /// <summary>Deletes an application.</summary>
    public Task DeleteApplicationAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}"), cancellationToken);

    /// <summary>The application's integration settings (issuer, discovery URL, client ID, …). Never contains secrets.</summary>
    public Task<ApplicationConfig> GetApplicationConfigAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken cancellationToken = default) =>
        SendAsync<ApplicationConfig>(HttpMethod.Get, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/config"), null, false, cancellationToken);

    /// <summary>
    /// Issues a new client secret. The previous one keeps working for the overlap period unless
    /// <paramref name="revokePrevious"/> is true (use that after a leak).
    /// </summary>
    public Task<RotatedSecret> RotateSecretAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, bool revokePrevious = false, CancellationToken cancellationToken = default) =>
        SendAsync<RotatedSecret>(HttpMethod.Post, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/secret{(revokePrevious ? "?revokePrevious=true" : string.Empty)}"), null, true, cancellationToken);

    /// <summary>Ends the overlap period: the previous client secret stops working now.</summary>
    public Task RevokePreviousSecretAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/secret/previous"), cancellationToken);

    // ---------------------------------------------------------------- Scopes, roles and users

    /// <summary>Lists the environment's OAuth scopes.</summary>
    public Task<IReadOnlyList<Scope>> ListScopesAsync(Guid organizationId, Guid projectId, Guid environmentId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<Scope>>(HttpMethod.Get, Env(organizationId, projectId, environmentId, "/scopes"), null, false, cancellationToken);

    /// <summary>Defines an OAuth scope (for example <c>orders:read</c>) in the environment.</summary>
    public Task<Scope> CreateScopeAsync(Guid organizationId, Guid projectId, Guid environmentId, string name, string? description = null, CancellationToken cancellationToken = default) =>
        SendAsync<Scope>(HttpMethod.Post, Env(organizationId, projectId, environmentId, "/scopes"), new { name, description }, true, cancellationToken);

    /// <summary>Deletes a scope no application uses.</summary>
    public Task DeleteScopeAsync(Guid organizationId, Guid projectId, Guid environmentId, string name, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Env(organizationId, projectId, environmentId, $"/scopes/{Uri.EscapeDataString(name)}"), cancellationToken);

    /// <summary>Creates an application role.</summary>
    public Task<Role> CreateRoleAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, string name, string? description = null, CancellationToken cancellationToken = default) =>
        SendAsync<Role>(HttpMethod.Post, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/roles"), new { name, description }, true, cancellationToken);

    /// <summary>Assigns an application role to a user; it appears in their next tokens.</summary>
    public Task AssignRoleAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, string role, string userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/roles/{Uri.EscapeDataString(role)}/users/{Uri.EscapeDataString(userId)}"), cancellationToken);

    /// <summary>Removes an application role from a user.</summary>
    public Task RemoveRoleAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, string role, string userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Env(organizationId, projectId, environmentId, $"/applications/{applicationId}/roles/{Uri.EscapeDataString(role)}/users/{Uri.EscapeDataString(userId)}"), cancellationToken);

    /// <summary>Finds end users of the environment, optionally by exact email.</summary>
    public Task<IReadOnlyList<EndUser>> FindUsersAsync(Guid organizationId, Guid projectId, Guid environmentId, string? email = null, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<EndUser>>(HttpMethod.Get, Env(organizationId, projectId, environmentId, email is null ? "/users" : $"/users?email={Uri.EscapeDataString(email)}"), null, false, cancellationToken);

    /// <summary>Signs a user out everywhere: every session and refresh token is revoked.</summary>
    public Task RevokeSessionsAsync(Guid organizationId, Guid projectId, Guid environmentId, string userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, Env(organizationId, projectId, environmentId, $"/users/{Uri.EscapeDataString(userId)}/sessions"), cancellationToken);

    // ---------------------------------------------------------------- Audit

    /// <summary>Reads the audit log, newest first. Pass <see cref="AuditPage.NextBefore"/> as <paramref name="before"/> for older events.</summary>
    public Task<AuditPage> GetAuditEventsAsync(Guid organizationId, string? source = null, DateTimeOffset? before = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { string.Create(CultureInfo.InvariantCulture, $"limit={limit}") };
        if (source is not null)
        {
            query.Add($"source={Uri.EscapeDataString(source)}");
        }

        if (before is { } b)
        {
            query.Add($"before={Uri.EscapeDataString(b.ToString("O", CultureInfo.InvariantCulture))}");
        }

        return SendAsync<AuditPage>(HttpMethod.Get, Org(organizationId, $"/audit-events?{string.Join('&', query)}"), null, false, cancellationToken);
    }

    // ---------------------------------------------------------------- Transport

    private static string Org(Guid organizationId, string suffix) => $"v1/organizations/{organizationId}{suffix}";

    private static string Env(Guid organizationId, Guid projectId, Guid environmentId, string suffix) =>
        Org(organizationId, $"/projects/{projectId}/environments/{environmentId}{suffix}");

    private async Task SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, null, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool idempotent, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, idempotent, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false))!;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, bool idempotent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        if (idempotent)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            Problem? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<Problem>(Json, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
            }

            throw new DovepeakApiException(
                response.StatusCode,
                problem?.Code,
                problem?.Detail ?? problem?.Title ?? $"The Management API returned {(int)response.StatusCode}.",
                problem?.Errors);
        }
    }

    private sealed record Problem(string? Title, string? Detail, string? Code, Dictionary<string, string[]>? Errors);
}
