using System.Net;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>Milestones M3.1 and M3.3 — authentication, organizations, roles and invitations.</summary>
[Trait("Category", "Integration")]
[Collection(ManagementApiGroup.Name)]
public sealed class OrganizationApiTests(ManagementApiFixture api)
{
    [Fact]
    public async Task UnauthenticatedRequests_AreRejected()
    {
        using var anonymous = api.Anonymous();
        (await anonymous.GetAsync("/v1/organizations")).Expect(HttpStatusCode.Unauthorized);

        using var forged = api.Client("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.");
        (await forged.GetAsync("/v1/organizations")).Expect(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthAndOpenApi_ArePublic()
    {
        using var anonymous = api.Anonymous();
        (await anonymous.GetAsync("/health/live")).Expect(HttpStatusCode.OK);
        var openApi = (await anonymous.GetAsync("/openapi/v1.json")).Expect(HttpStatusCode.OK);
        Assert.Contains("/v1/organizations/{orgId}/projects", openApi.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creator_BecomesOwner_AndSeesOrganization()
    {
        using var developer = await api.NewDeveloperAsync();
        var slug = $"acme-{Guid.NewGuid():N}"[..20];

        var created = (await developer.PostAsync("/v1/organizations", new { slug, name = "Acme" })).Expect(HttpStatusCode.Created).Json;
        var mine = (await developer.GetAsync("/v1/organizations")).Expect(HttpStatusCode.OK).Json.AsArray();

        Assert.Equal("owner", created["role"]!.GetValue<string>());
        Assert.Contains(mine, o => o!["slug"]!.GetValue<string>() == slug);
        (await developer.PostAsync("/v1/organizations", new { slug, name = "Duplicate" })).Expect(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidInput_ReturnsProblemDetails()
    {
        using var developer = await api.NewDeveloperAsync();

        var response = (await developer.PostAsync("/v1/organizations", new { slug = "Not A Slug!", name = "x" })).Expect(HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", response.Code);
        Assert.NotNull(response.Json["errors"]!["slug"]);
    }

    [Fact]
    public async Task NonMember_GetsNotFound_NotForbidden()
    {
        var scenario = await Scenario.CreateAsync(api);
        using var outsider = await api.NewDeveloperAsync();

        var response = (await outsider.GetAsync(scenario.Org)).Expect(HttpStatusCode.NotFound);
        Assert.Equal("organization_not_found", response.Code);
    }

    [Fact]
    public async Task Invitation_IsAcceptedOnlyByTheInvitedVerifiedEmail_WithTheInvitedRole()
    {
        var scenario = await Scenario.CreateAsync(api);
        using var invitee = await api.NewDeveloperAsync();
        using var someoneElse = await api.NewDeveloperAsync();

        var invitation = (await scenario.Owner.PostAsync($"{scenario.Org}/invitations", new { email = invitee.Email, role = "viewer" }))
            .Expect(HttpStatusCode.Created).Json;
        var invitationId = invitation["id"]!.GetValue<string>();

        // Another developer cannot see or accept it.
        Assert.Empty((await someoneElse.GetAsync("/v1/me/invitations")).Expect(HttpStatusCode.OK).Json.AsArray());
        (await someoneElse.PostAsync($"/v1/me/invitations/{invitationId}/accept")).Expect(HttpStatusCode.NotFound);

        Assert.Single((await invitee.GetAsync("/v1/me/invitations")).Expect(HttpStatusCode.OK).Json.AsArray());
        (await invitee.PostAsync($"/v1/me/invitations/{invitationId}/accept")).Expect(HttpStatusCode.OK);

        // A viewer can read but not change anything.
        (await invitee.GetAsync($"{scenario.Org}/projects")).Expect(HttpStatusCode.OK);
        var denied = (await invitee.PostAsync($"{scenario.Org}/projects", new { slug = "blocked", name = "Blocked" })).Expect(HttpStatusCode.Forbidden);
        Assert.Equal("permission_denied", denied.Code);
    }

    [Fact]
    public async Task LastOwner_CannotLeaveOrBeDemoted()
    {
        var scenario = await Scenario.CreateAsync(api);

        var leave = (await scenario.Owner.DeleteAsync($"{scenario.Org}/members/{scenario.Owner.UserId}")).Expect(HttpStatusCode.Conflict);
        Assert.Equal("last_owner", leave.Code);

        var demote = (await scenario.Owner.PatchAsync($"{scenario.Org}/members/{scenario.Owner.UserId}", new { role = "admin" })).Expect(HttpStatusCode.Conflict);
        Assert.Equal("last_owner", demote.Code);
    }

    [Fact]
    public async Task Admin_CannotGrantOwnership()
    {
        var scenario = await Scenario.CreateAsync(api);
        using var admin = await JoinAsync(scenario, "admin");
        using var developer = await JoinAsync(scenario, "developer");

        (await admin.PatchAsync($"{scenario.Org}/members/{developer.UserId}", new { role = "owner" })).Expect(HttpStatusCode.Forbidden);
        (await admin.PatchAsync($"{scenario.Org}/members/{developer.UserId}", new { role = "admin" })).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrganizationWithProjects_CannotBeDeleted()
    {
        var scenario = await Scenario.CreateAsync(api);
        var response = (await scenario.Owner.DeleteAsync(scenario.Org)).Expect(HttpStatusCode.Conflict);
        Assert.Equal("organization_not_empty", response.Code);
    }

    private async Task<ApiClient> JoinAsync(Scenario scenario, string role)
    {
        var member = await api.NewDeveloperAsync();
        var invitation = (await scenario.Owner.PostAsync($"{scenario.Org}/invitations", new { email = member.Email, role })).Expect(HttpStatusCode.Created).Json;
        (await member.PostAsync($"/v1/me/invitations/{invitation["id"]!.GetValue<string>()}/accept")).Expect(HttpStatusCode.OK);
        return member;
    }
}
