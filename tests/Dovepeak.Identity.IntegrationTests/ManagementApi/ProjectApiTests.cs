using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestone M3.4 — projects provision three isolated environments through the outbox.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class ProjectApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task CreatingProject_ProvisionsThreeIsolatedRealms()
    {
        using var owner = await api.NewDeveloperAsync();
        var org = (await owner.PostAsync("/v1/organizations", new { slug = $"p-{Guid.NewGuid():N}"[..20], name = "P" })).Expect(HttpStatusCode.Created).Json;
        var orgPath = $"/v1/organizations/{org["id"]!.GetValue<string>()}";

        var project = (await owner.PostAsync($"{orgPath}/projects", new { slug = "web", name = "Web" })).Expect(HttpStatusCode.Created).Json;
        var environments = project["environments"]!.AsArray();

        Assert.Equal(["development", "staging", "production"], environments.Select(e => e!["kind"]!.GetValue<string>()));
        Assert.All(environments, e => Assert.Equal("pending", e!["state"]!.GetValue<string>()));

        await api.ProcessOutboxAsync();

        var after = (await owner.GetAsync($"{orgPath}/projects/{project["id"]!.GetValue<string>()}")).Expect(HttpStatusCode.OK).Json;
        var issuers = after["environments"]!.AsArray().Select(e => e!["issuer"]!.GetValue<string>()).ToList();
        Assert.All(after["environments"]!.AsArray(), e => Assert.Equal("ready", e!["state"]!.GetValue<string>()));
        Assert.Equal(3, issuers.Distinct().Count());

        // Each environment is a real, separate identity directory with the secure baseline.
        foreach (var environment in after["environments"]!.AsArray())
        {
            var realm = RealmName.ForEnvironment(Guid.Parse(environment!["id"]!.GetValue<string>()));
            var representation = await KeycloakAdmin.Client.GetRealmAsync(realm, CancellationToken.None);
            Assert.NotNull(representation);
            Assert.True(representation["bruteForceProtected"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task DeletingProject_RemovesItsRealms()
    {
        var scenario = await Scenario.CreateAsync(api);
        var realm = RealmName.ForEnvironment(scenario.DevelopmentId);
        Assert.NotNull(await KeycloakAdmin.Client.GetRealmAsync(realm, CancellationToken.None));

        (await scenario.Owner.DeleteAsync(scenario.Project)).Expect(HttpStatusCode.NoContent);
        await api.ProcessOutboxAsync();

        Assert.Null(await KeycloakAdmin.Client.GetRealmAsync(realm, CancellationToken.None));
        (await scenario.Owner.GetAsync(scenario.Project)).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProjectWithApplications_CannotBeDeleted()
    {
        var scenario = await Scenario.CreateAsync(api);
        await scenario.CreateApplicationAsync("spa");

        var response = (await scenario.Owner.DeleteAsync(scenario.Project)).Expect(HttpStatusCode.Conflict);
        Assert.Equal("project_not_empty", response.Code);
    }

    [Fact]
    public async Task ProjectQuota_IsEnforced()
    {
        // The test host allows 3 projects per organization; the scenario already created one.
        var scenario = await Scenario.CreateAsync(api);
        (await scenario.Owner.PostAsync($"{scenario.Org}/projects", new { slug = "two", name = "Two" })).Expect(HttpStatusCode.Created);
        (await scenario.Owner.PostAsync($"{scenario.Org}/projects", new { slug = "three", name = "Three" })).Expect(HttpStatusCode.Created);

        var response = (await scenario.Owner.PostAsync($"{scenario.Org}/projects", new { slug = "four", name = "Four" })).Expect(HttpStatusCode.Conflict);
        Assert.Equal("quota_exceeded", response.Code);
        await api.ProcessOutboxAsync();
    }

    [Fact]
    public async Task EnvironmentMapping_IsStoredWithCluster()
    {
        var scenario = await Scenario.CreateAsync(api);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        db.TenantScope.EnterOrganization(scenario.OrganizationId);
        var environment = await db.Environments.SingleAsync(e => e.Id == scenario.DevelopmentId);

        Assert.Equal("default", environment.Cluster);
        Assert.Equal(ProvisioningState.Ready, environment.State);
    }
}
