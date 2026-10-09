extern alias ManagementApi;

using System.Net.Http.Headers;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Platform.Outbox;
using Dovepeak.Identity.Platform.Reconciliation;
using Dovepeak.Identity.Platform.Security;
using Dovepeak.Identity.Platform.Webhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

[CollectionDefinition(Name)]
public sealed class ManagementApiGroup : ICollectionFixture<ManagementApiFixture>
{
    public const string Name = "Management API";
}

/// <summary>
/// Hosts the real Management API in-process against the live stack. Developers sign in through the real platform realm
/// and portal client; background processors (outbox, webhooks, reconciliation) are driven explicitly by tests.
/// </summary>
public sealed class ManagementApiFixture : IAsyncLifetime
{
    public static readonly Uri PortalRedirectUri = new("http://localhost:3999/callback");
    private const string PlatformRealm = "dovepeak-platform";

    private readonly WebApplicationFactory<ManagementApi::Program> _factory;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public ManagementApiFixture()
    {
        var settings = StackSettings.Current;
        _factory = new WebApplicationFactory<ManagementApi::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", settings.PostgresConnection);
            builder.UseSetting("Keycloak:BaseUrl", settings.KeycloakAdminUrl.ToString());
            builder.UseSetting("Keycloak:ClientId", settings.ManagementClientId);
            builder.UseSetting("Keycloak:ClientSecret", settings.ManagementClientSecret);
            builder.UseSetting("Platform:PublicIdentityUrl", settings.KeycloakUrl.ToString());
            builder.UseSetting("Platform:Quotas:ProjectsPerOrganization", "3");
            builder.UseSetting("Platform:Quotas:OrganizationsPerDeveloper", "50");
        });
    }

    public IServiceProvider Services => _factory.Services;

    public static Uri PlatformIssuer => new(StackSettings.Current.KeycloakUrl, $"realms/{PlatformRealm}");

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PlatformRealmInitializer>().EnsureAsync(CancellationToken.None);
    }

    /// <summary>
    /// Removes every organization created during this run, then its realms. Database rows go first: a "ready"
    /// environment whose realm is missing is drift, and the reconciler would recreate the realm.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.PlatformDbContext>();
            db.TenantScope.EnterSystem();
            var organizations = await db.Organizations.Where(o => o.CreatedAt >= _startedAt).Select(o => o.Id).ToListAsync();
            var realms = await db.Environments.Where(e => organizations.Contains(e.OrganizationId)).Select(e => e.RealmName).ToListAsync();

            await db.OutboxMessages.Where(m => m.OrganizationId != null && organizations.Contains(m.OrganizationId.Value)).ExecuteDeleteAsync();
            await db.Applications.Where(a => organizations.Contains(a.OrganizationId)).ExecuteDeleteAsync();
            await db.Environments.Where(e => organizations.Contains(e.OrganizationId)).ExecuteDeleteAsync();
            await db.Projects.Where(p => organizations.Contains(p.OrganizationId)).ExecuteDeleteAsync();
            await db.Organizations.Where(o => organizations.Contains(o.Id)).ExecuteDeleteAsync();

            foreach (var realm in realms)
            {
                await KeycloakAdmin.Client.DeleteRealmAsync(RealmName.Parse(realm), CancellationToken.None);
            }
        }

        _factory.Dispose();
    }

    /// <summary>Creates a verified developer in the platform realm and signs them in through the portal client.</summary>
    public async Task<ApiClient> NewDeveloperAsync()
    {
        var developer = TestUser.Generate();
        var id = await KeycloakAdmin.Client.CreateUserAsync(RealmName.Parse(PlatformRealm), developer.Email, developer.Password, emailVerified: true, CancellationToken.None);
        using var portal = new OidcClient(PlatformIssuer, "dovepeak-portal", clientSecret: null, PortalRedirectUri);
        var tokens = await SignIn.AsAsync(developer, portal);
        return Client(tokens.AccessToken, id, developer.Email);
    }

    public ApiClient Client(string bearer, string? userId = null, string? email = null)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return new ApiClient(http, userId, email);
    }

    public ApiClient Anonymous() => new(_factory.CreateClient(), null, null);

    public async Task ProcessOutboxAsync()
    {
        var processor = _factory.Services.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessDueAsync(CancellationToken.None) > 0)
        {
        }
    }

    public async Task DeliverWebhooksAsync()
    {
        await ProcessOutboxAsync();
        var dispatcher = _factory.Services.GetRequiredService<WebhookDispatcher>();
        while (await dispatcher.DeliverDueAsync(CancellationToken.None) > 0)
        {
        }
    }

    public Task<IReadOnlyList<DriftCorrection>> ReconcileAsync(Guid environmentId) =>
        _factory.Services.GetRequiredService<ReconciliationService>().ReconcileEnvironmentAsync(environmentId, CancellationToken.None);
}
