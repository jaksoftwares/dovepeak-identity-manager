using System.Net;
using System.Text.Json.Nodes;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>An organization with one provisioned project, built entirely through the public Management API.</summary>
public sealed record Scenario(ApiClient Owner, Guid OrganizationId, Guid ProjectId, Guid DevelopmentId, Guid ProductionId)
{
    public string Org => $"/v1/organizations/{OrganizationId}";

    public string Project => $"{Org}/projects/{ProjectId}";

    public string Development => $"{Project}/environments/{DevelopmentId}";

    public static async Task<Scenario> CreateAsync(ManagementApiFixture api, ApiClient? owner = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        owner ??= await api.NewDeveloperAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var org = (await owner.PostAsync("/v1/organizations", new { slug = $"org-{suffix}", name = $"Org {suffix}" })).Expect(HttpStatusCode.Created).Json;
        var orgId = Guid.Parse(org["id"]!.GetValue<string>());

        var project = (await owner.PostAsync($"/v1/organizations/{orgId}/projects", new { slug = "shop", name = "Shop" })).Expect(HttpStatusCode.Created).Json;
        var projectId = Guid.Parse(project["id"]!.GetValue<string>());
        await WaitUntilReadyAsync(api, owner, $"/v1/organizations/{orgId}/projects/{projectId}");

        var environments = project["environments"]!.AsArray();
        return new Scenario(owner, orgId, projectId, EnvironmentId(environments, "development"), EnvironmentId(environments, "production"));
    }

    /// <summary>
    /// Drives the outbox, then waits until the API reports every environment ready. The deployed workers container
    /// polls the same outbox and may hold the provisioning message, so an empty in-process run does not mean the realms
    /// exist yet.
    /// </summary>
    private static async Task WaitUntilReadyAsync(ManagementApiFixture api, ApiClient owner, string projectPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (true)
        {
            await api.ProcessOutboxAsync();
            var environments = (await owner.GetAsync(projectPath)).Expect(HttpStatusCode.OK).Json["environments"]!.AsArray();
            if (environments.All(e => e!["state"]!.GetValue<string>() == "ready"))
            {
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Environments of {projectPath} were not ready after 2 minutes.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public async Task<JsonNode> CreateApplicationAsync(string kind, string? name = null, string[]? audiences = null)
    {
        object body = kind == "machine"
            ? new { name = name ?? $"svc-{Guid.NewGuid():N}"[..20], kind, audiences = audiences ?? [] }
            : new
            {
                name = name ?? $"app-{Guid.NewGuid():N}"[..20],
                kind,
                redirectUris = new[] { "http://localhost:3999/callback" },
                postLogoutRedirectUris = new[] { "http://localhost:3999/" },
                webOrigins = new[] { "http://localhost:3999" },
                audiences = audiences ?? ["dovepeak-demo-api"],
            };

        return (await Owner.PostAsync($"{Development}/applications", body)).Expect(HttpStatusCode.Created).Json;
    }

    private static Guid EnvironmentId(JsonArray environments, string kind) =>
        Guid.Parse(environments.Single(e => e!["kind"]!.GetValue<string>() == kind)!["id"]!.GetValue<string>());
}
