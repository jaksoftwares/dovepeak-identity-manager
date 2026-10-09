using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.IntegrationTests.ManagementApi;
using Dovepeak.Identity.Keycloak;

namespace Dovepeak.Identity.IntegrationTests;

/// <summary>
/// The deployed stack, end to end, with no in-process shortcuts: a developer signs in through the platform realm,
/// calls the containerized Management API over HTTP, and the containerized workers provision and delete realms
/// through the outbox on their own schedule. Everything else in the suite drives the background jobs explicitly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeployedStackTests
{
    private static readonly TimeSpan ProvisioningTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Developer_OnboardsAProject_AndIssuesTokens_ThroughTheDeployedServices()
    {
        using var developer = await SignInDeveloperAsync();

        // Organization and project: the API only records intent; the workers container provisions the realms.
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var org = (await developer.PostAsync("/v1/organizations", new { slug = $"org-{suffix}", name = $"Org {suffix}" })).Expect(HttpStatusCode.Created).Json;
        var orgPath = $"/v1/organizations/{org["id"]!.GetValue<string>()}";
        var project = (await developer.PostAsync($"{orgPath}/projects", new { slug = "deployed", name = "Deployed" })).Expect(HttpStatusCode.Created).Json;
        var projectPath = $"{orgPath}/projects/{project["id"]!.GetValue<string>()}";

        var environments = await WaitForEnvironmentsAsync(developer, projectPath, state: "ready");
        var development = environments.Single(e => e["kind"]!.GetValue<string>() == "development");
        var envPath = $"{projectPath}/environments/{development["id"]!.GetValue<string>()}";
        var realms = environments.Select(e => RealmName.ForEnvironment(Guid.Parse(e["id"]!.GetValue<string>()))).ToList();

        // Configure a scope and a machine application, then obtain a token from the tenant's issuer.
        (await developer.PostAsync($"{envPath}/scopes", new { name = "orders:read" })).Expect(HttpStatusCode.Created);
        var app = (await developer.PostAsync($"{envPath}/applications", new
        {
            name = "deployed-service",
            kind = "machine",
            audiences = new[] { ExampleApi.Audience },
            scopes = new[] { "orders:read" },
        })).Expect(HttpStatusCode.Created).Json;

        var issuer = new Uri(development["issuer"]!.GetValue<string>().TrimEnd('/') + "/");
        using (var client = new OidcClient(issuer, app["application"]!["clientId"]!.GetValue<string>(), app["clientSecret"]!.GetValue<string>(), TestRealm.RedirectUri))
        using (var response = await client.PostTokenAsync(new() { ["grant_type"] = "client_credentials", ["scope"] = "orders:read" }))
        {
            response.EnsureSuccessStatusCode();
            var token = new JwtSecurityToken((await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>());
            Assert.Contains("orders:read", token.Claims.Single(c => c.Type == "scope").Value.Split(' '));
            Assert.Contains(ExampleApi.Audience, token.Audiences);
        }

        // Tear down through the API: the workers delete the realms.
        (await developer.DeleteAsync($"{envPath}/applications/{app["application"]!["id"]!.GetValue<string>()}")).Expect(HttpStatusCode.NoContent);
        (await developer.DeleteAsync(projectPath)).Expect(HttpStatusCode.NoContent);
        await WaitUntilAsync(async () =>
        {
            foreach (var realm in realms)
            {
                if (await KeycloakAdmin.Client.GetRealmAsync(realm, CancellationToken.None) is not null)
                {
                    return false;
                }
            }

            return true;
        }, "the workers to delete the project's realms");

        (await developer.DeleteAsync(orgPath)).Expect(HttpStatusCode.NoContent);
    }

    private static async Task<ApiClient> SignInDeveloperAsync()
    {
        var user = TestUser.Generate();
        var id = await KeycloakAdmin.Client.CreateUserAsync(RealmName.Parse(ManagementApiFixture.PlatformRealm), user.Email, user.Password, emailVerified: true, CancellationToken.None);
        using var portal = new OidcClient(ManagementApiFixture.PlatformIssuer, "dovepeak-portal", clientSecret: null, ManagementApiFixture.PortalRedirectUri);
        var tokens = await SignIn.AsAsync(user, portal);

        var http = new HttpClient { BaseAddress = StackSettings.Current.ManagementApiUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return new ApiClient(http, id, user.Email);
    }

    private static async Task<List<JsonNode>> WaitForEnvironmentsAsync(ApiClient developer, string projectPath, string state)
    {
        List<JsonNode> environments = [];
        await WaitUntilAsync(async () =>
        {
            environments = [.. (await developer.GetAsync(projectPath)).Expect(HttpStatusCode.OK).Json["environments"]!.AsArray().Select(e => e!)];
            return environments.Count == 3 && environments.All(e => e["state"]!.GetValue<string>() == state);
        }, $"all three environments to become {state}");
        return environments;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (stopwatch.Elapsed > ProvisioningTimeout)
            {
                Assert.Fail($"Timed out after {ProvisioningTimeout} waiting for {description}. Is the workers container running?");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
