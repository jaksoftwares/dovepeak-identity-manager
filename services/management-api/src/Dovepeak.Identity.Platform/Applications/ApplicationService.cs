using System.Security.Cryptography;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Audit;
using Dovepeak.Identity.Platform.Common;
using Dovepeak.Identity.Platform.Projects;
using Dovepeak.Identity.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Platform.Applications;

/// <summary>
/// Token and session lifetimes in seconds. Omitted (null) values inherit the environment's baseline: 600 s access
/// tokens, 1800 s session idle timeout, 43200 s session maximum.
/// </summary>
public sealed record ApplicationTokenPolicy(int? AccessTokenLifetimeSeconds, int? SessionIdleTimeoutSeconds, int? SessionMaxLifetimeSeconds);

public sealed record ApplicationSettings(
    string Name,
    ApplicationKind Kind,
    IReadOnlyList<string>? RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string>? WebOrigins,
    IReadOnlyList<string>? Audiences,
    ApplicationTokenPolicy? TokenPolicy = null,
    IReadOnlyList<string>? Scopes = null);

/// <summary>Partial update. A supplied <see cref="TokenPolicy"/> replaces the whole policy; omitted fields in it revert to the baseline.</summary>
public sealed record ApplicationUpdate(
    string? Name,
    IReadOnlyList<string>? RedirectUris,
    IReadOnlyList<string>? PostLogoutRedirectUris,
    IReadOnlyList<string>? WebOrigins,
    IReadOnlyList<string>? Audiences,
    ApplicationTokenPolicy? TokenPolicy = null,
    IReadOnlyList<string>? Scopes = null);

public sealed record ApplicationView(
    Guid Id,
    Guid ProjectId,
    Guid EnvironmentId,
    string Name,
    ApplicationKind Kind,
    string ClientId,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> WebOrigins,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> Scopes,
    ApplicationTokenPolicy TokenPolicy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Returned once, when an application is created or its secret is rotated (ADR-0004).</summary>
public sealed record ApplicationWithSecret(ApplicationView Application, string? ClientSecret);

/// <summary>
/// A newly issued client secret. The previous secret keeps working until <see cref="PreviousSecretExpiresAt"/> so
/// deployments can switch without downtime; null means it stopped working immediately.
/// </summary>
public sealed record RotatedSecret(ApplicationView Application, string ClientSecret, DateTimeOffset? PreviousSecretExpiresAt);

/// <summary>Everything a client needs to integrate. Never contains secrets (problem statement §16).</summary>
public sealed record ApplicationConfigView(
    string ClientId,
    ApplicationKind Kind,
    string Issuer,
    string DiscoveryUrl,
    string TokenEndpointAuthMethod,
    bool PkceRequired,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    IReadOnlyList<string> WebOrigins,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> Scopes,
    int AccessTokenLifetimeSeconds,
    int? SessionIdleTimeoutSeconds,
    int? SessionMaxLifetimeSeconds);

/// <summary>Applications (OAuth clients) inside a project environment (milestones M3.5, M3.6).</summary>
public sealed class ApplicationService(
    PlatformDbContext db,
    TenantAuthorizer authorizer,
    ManagementAuditLog audit,
    KeycloakAdminClient engine,
    IOptions<PlatformOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ApplicationWithSecret> CreateAsync(Guid organizationId, Guid projectId, Guid environmentId, ApplicationSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsWrite, ct);
        var environment = await ReadyEnvironmentAsync(projectId, environmentId, ct);

        var limit = options.Value.Quotas.ApplicationsPerEnvironment;
        if (await db.Applications.CountAsync(a => a.EnvironmentId == environmentId, ct) >= limit)
        {
            throw PlatformException.Quota("applications in this environment", limit);
        }

        var now = timeProvider.GetUtcNow();
        var application = new Application
        {
            OrganizationId = organizationId,
            ProjectId = projectId,
            EnvironmentId = environmentId,
            Name = Slug.ValidateName(settings.Name, "name"),
            Kind = settings.Kind,
            ClientId = NewClientId(),
            RedirectUris = [.. settings.RedirectUris ?? []],
            PostLogoutRedirectUris = [.. settings.PostLogoutRedirectUris ?? []],
            WebOrigins = [.. settings.WebOrigins ?? []],
            Audiences = [.. settings.Audiences ?? []],
            Scopes = [.. (settings.Scopes ?? []).Distinct(StringComparer.Ordinal)],
            AccessTokenLifetimeSeconds = settings.TokenPolicy?.AccessTokenLifetimeSeconds,
            SessionIdleTimeoutSeconds = settings.TokenPolicy?.SessionIdleTimeoutSeconds,
            SessionMaxLifetimeSeconds = settings.TokenPolicy?.SessionMaxLifetimeSeconds,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var registration = ToRegistration(application);
        var environmentScopes = await RequireScopesAsync(environmentId, application.Scopes, ct);
        if (await db.Applications.AnyAsync(a => a.EnvironmentId == environmentId && a.Name == application.Name, ct))
        {
            throw PlatformException.Conflict("name_taken", "An application with this name already exists in the environment.");
        }

        var realm = RealmName.Parse(environment.RealmName);
        var client = await CallEngineAsync(() => engine.CreateClientAsync(realm, registration, ct));
        application.EngineClientId = client.Id;

        db.Applications.Add(application);
        audit.Record(organizationId, "application.created", "application", application.Id,
            new { application.Name, application.Kind, application.ClientId, environment = environment.Kind, tokenPolicy = PolicyOf(application) });

        try
        {
            await CallEngineAsync(() => ScopeService.SyncClientScopesAsync(engine, realm, client.Id, application.Scopes, environmentScopes, ct));
            await db.SaveOrConflictAsync(ct, "name_taken", "An application with this name already exists in the environment.");
        }
        catch
        {
            // Compensate: never leave an engine client without a platform record.
            await engine.DeleteClientAsync(realm, client.Id, CancellationToken.None);
            throw;
        }

        return new ApplicationWithSecret(ToView(application), client.Secret);
    }

    public async Task<IReadOnlyList<ApplicationView>> ListAsync(Guid organizationId, Guid projectId, Guid environmentId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsRead, ct);
        await EnvironmentAsync(projectId, environmentId, ct);
        var applications = await db.Applications.Where(a => a.EnvironmentId == environmentId).OrderBy(a => a.Name).ToListAsync(ct);
        return applications.Select(ToView).ToList();
    }

    public async Task<ApplicationView> GetAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsRead, ct);
        return ToView(await FindAsync(projectId, environmentId, applicationId, ct));
    }

    public async Task<ApplicationView> UpdateAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, ApplicationUpdate update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsWrite, ct);
        var application = await FindAsync(projectId, environmentId, applicationId, ct);
        var environment = await ReadyEnvironmentAsync(projectId, environmentId, ct);

        if (update.Name is not null)
        {
            application.Name = Slug.ValidateName(update.Name, "name");
        }

        application.RedirectUris = update.RedirectUris?.ToList() ?? application.RedirectUris;
        application.PostLogoutRedirectUris = update.PostLogoutRedirectUris?.ToList() ?? application.PostLogoutRedirectUris;
        application.WebOrigins = update.WebOrigins?.ToList() ?? application.WebOrigins;
        application.Audiences = update.Audiences?.ToList() ?? application.Audiences;
        if (update.Scopes is not null)
        {
            application.Scopes = [.. update.Scopes.Distinct(StringComparer.Ordinal)];
        }

        if (update.TokenPolicy is { } policy)
        {
            application.AccessTokenLifetimeSeconds = policy.AccessTokenLifetimeSeconds;
            application.SessionIdleTimeoutSeconds = policy.SessionIdleTimeoutSeconds;
            application.SessionMaxLifetimeSeconds = policy.SessionMaxLifetimeSeconds;
        }

        application.UpdatedAt = timeProvider.GetUtcNow();

        var registration = ToRegistration(application);
        var environmentScopes = await RequireScopesAsync(environmentId, application.Scopes, ct);
        var realm = RealmName.Parse(environment.RealmName);
        await CallEngineAsync(() => engine.UpdateClientAsync(realm, application.EngineClientId!, registration, ct));
        await CallEngineAsync(() => ScopeService.SyncClientScopesAsync(engine, realm, application.EngineClientId!, application.Scopes, environmentScopes, ct));

        audit.Record(organizationId, "application.updated", "application", application.Id, new
        {
            application.RedirectUris,
            application.PostLogoutRedirectUris,
            application.WebOrigins,
            application.Audiences,
            application.Scopes,
            tokenPolicy = PolicyOf(application),
        });
        await db.SaveOrConflictAsync(ct, "name_taken", "An application with this name already exists in the environment.");
        return ToView(application);
    }

    public async Task DeleteAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsWrite, ct);
        var application = await FindAsync(projectId, environmentId, applicationId, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);

        if (application.EngineClientId is not null)
        {
            await CallEngineAsync(() => engine.DeleteClientAsync(RealmName.Parse(environment.RealmName), application.EngineClientId, ct));
        }

        db.Applications.Remove(application);
        audit.Record(organizationId, "application.deleted", "application", application.Id, new { application.ClientId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Issues a new client secret. By default the previous secret keeps working for the overlap period (24 hours) so
    /// the application can be redeployed without downtime; <paramref name="revokePrevious"/> ends it immediately,
    /// which is the right response to a leaked secret.
    /// </summary>
    public async Task<RotatedSecret> RotateSecretAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, bool revokePrevious, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.CredentialsManage, ct);
        var (application, realm) = await ConfidentialApplicationAsync(projectId, environmentId, applicationId, ct);

        var secret = await CallEngineAsync(() => engine.RegenerateClientSecretAsync(realm, application.EngineClientId!, ct));
        if (revokePrevious)
        {
            await CallEngineAsync(() => engine.RevokePreviousSecretAsync(realm, application.EngineClientId!, ct));
        }

        var previousExpiresAt = revokePrevious ? null : await CallEngineAsync(() => engine.GetPreviousSecretExpiryAsync(realm, application.EngineClientId!, ct));
        audit.Record(organizationId, "application.secret_rotated", "application", application.Id,
            new { application.ClientId, previousSecretExpiresAt = previousExpiresAt });
        await db.SaveChangesAsync(ct);
        return new RotatedSecret(ToView(application), secret, previousExpiresAt);
    }

    /// <summary>Ends the overlap period: only the current secret is accepted from now on.</summary>
    public async Task RevokePreviousSecretAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.CredentialsManage, ct);
        var (application, realm) = await ConfidentialApplicationAsync(projectId, environmentId, applicationId, ct);

        await CallEngineAsync(() => engine.RevokePreviousSecretAsync(realm, application.EngineClientId!, ct));
        audit.Record(organizationId, "application.previous_secret_revoked", "application", application.Id, new { application.ClientId });
        await db.SaveChangesAsync(ct);
    }

    private async Task<(Application Application, RealmName Realm)> ConfidentialApplicationAsync(Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        var application = await FindAsync(projectId, environmentId, applicationId, ct);
        if (application.Kind is ApplicationKind.Spa or ApplicationKind.Native)
        {
            throw PlatformException.Conflict("public_client", "Public clients (SPA and native) have no client secret.");
        }

        var environment = await ReadyEnvironmentAsync(projectId, environmentId, ct);
        return (application, RealmName.Parse(environment.RealmName));
    }

    public async Task<ApplicationConfigView> GetConfigAsync(Guid organizationId, Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct)
    {
        await authorizer.AuthorizeAsync(organizationId, Permission.ApplicationsRead, ct);
        var application = await FindAsync(projectId, environmentId, applicationId, ct);
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        var issuer = ProjectService.Issuer(options.Value, environment.RealmName).TrimEnd('/');
        var policy = ToRegistration(application).TokenPolicy;
        var interactive = application.Kind != ApplicationKind.Machine;

        return new ApplicationConfigView(
            application.ClientId,
            application.Kind,
            issuer,
            $"{issuer}/.well-known/openid-configuration",
            application.Kind is ApplicationKind.Web or ApplicationKind.Machine ? "client_secret_post" : "none",
            PkceRequired: application.Kind != ApplicationKind.Machine,
            application.RedirectUris,
            application.PostLogoutRedirectUris,
            application.WebOrigins,
            application.Audiences,
            application.Scopes,
            policy.EffectiveAccessTokenLifetimeSeconds,
            interactive ? policy.EffectiveSessionIdleTimeoutSeconds : null,
            interactive ? policy.EffectiveSessionMaxLifetimeSeconds : null);
    }

    internal static ClientRegistration ToRegistration(Application application)
    {
        var kind = application.Kind switch
        {
            ApplicationKind.Spa or ApplicationKind.Native => ClientKind.Public,
            ApplicationKind.Web => ClientKind.Confidential,
            _ => ClientKind.Machine,
        };

        var policy = new TokenPolicy(application.AccessTokenLifetimeSeconds, application.SessionIdleTimeoutSeconds, application.SessionMaxLifetimeSeconds);
        try
        {
            policy.Validate(kind);
        }
        catch (ArgumentException ex)
        {
            throw PlatformException.Invalid("tokenPolicy", ex.Message);
        }

        try
        {
            var registration = new ClientRegistration(application.ClientId, kind)
            {
                Name = application.Name,
                RedirectUris = application.RedirectUris.Select(u => ParseUri(u, "redirectUris")).ToList(),
                PostLogoutRedirectUris = application.PostLogoutRedirectUris.Select(u => ParseUri(u, "postLogoutRedirectUris")).ToList(),
                WebOrigins = application.WebOrigins.Select(u => ParseUri(u, "webOrigins")).ToList(),
                Audiences = application.Audiences.Select(ValidateAudience).ToList(),
                TokenPolicy = policy,
            };
            registration.Validate();
            return registration;
        }
        catch (ArgumentException ex)
        {
            throw PlatformException.Invalid("application", ex.Message);
        }
    }

    private static Uri ParseUri(string value, string field) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : throw PlatformException.Invalid(field, $"'{value}' is not an absolute URI.");

    private static string ValidateAudience(string audience) =>
        !string.IsNullOrWhiteSpace(audience) && audience.Length <= 200 && !audience.Any(char.IsWhiteSpace)
            ? audience
            : throw PlatformException.Invalid("audiences", "Audiences are non-empty identifiers without spaces (max 200 characters).");

    /// <summary>Public client identifier: "app_" followed by 24 random lowercase base-32 characters.</summary>
    private static string NewClientId()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        return "app_" + string.Create(24, alphabet, (chars, a) =>
        {
            Span<byte> bytes = stackalloc byte[24];
            RandomNumberGenerator.Fill(bytes);
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = a[bytes[i] % 32];
            }
        });
    }

    private async Task<ProjectEnvironment> EnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken ct) =>
        await db.Environments.SingleOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct)
        ?? throw PlatformException.NotFound("environment");

    private async Task<ProjectEnvironment> ReadyEnvironmentAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var environment = await EnvironmentAsync(projectId, environmentId, ct);
        return environment.State == ProvisioningState.Ready
            ? environment
            : throw PlatformException.Conflict("environment_not_ready", $"The environment is {environment.State.ToString().ToLowerInvariant()}; try again when it is ready.");
    }

    private async Task<Application> FindAsync(Guid projectId, Guid environmentId, Guid applicationId, CancellationToken ct) =>
        await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.ProjectId == projectId && a.EnvironmentId == environmentId, ct)
        ?? throw PlatformException.NotFound("application");

    internal static async Task<T> CallEngineAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException or TaskCanceledException)
        {
            throw PlatformException.Unavailable("The identity engine did not complete the operation. Try again shortly.");
        }
    }

    internal static Task CallEngineAsync(Func<Task> call) => CallEngineAsync(async () =>
    {
        await call();
        return true;
    });

    internal static ApplicationView ToView(Application a) => new(
        a.Id, a.ProjectId, a.EnvironmentId, a.Name, a.Kind, a.ClientId, a.RedirectUris, a.PostLogoutRedirectUris, a.WebOrigins,
        a.Audiences, a.Scopes, PolicyOf(a), a.CreatedAt, a.UpdatedAt);

    /// <summary>Every requested scope must be defined on the environment first.</summary>
    private async Task<IReadOnlyList<EnvironmentScope>> RequireScopesAsync(Guid environmentId, IReadOnlyCollection<string> requested, CancellationToken ct)
    {
        var defined = await db.EnvironmentScopes.Where(s => s.EnvironmentId == environmentId).ToListAsync(ct);
        var unknown = requested.Except(defined.Select(s => s.Name), StringComparer.Ordinal).ToList();
        return unknown.Count == 0
            ? defined
            : throw PlatformException.Invalid("scopes", $"Unknown scopes: {string.Join(", ", unknown)}. Define them on the environment first.");
    }

    private static ApplicationTokenPolicy PolicyOf(Application a) =>
        new(a.AccessTokenLifetimeSeconds, a.SessionIdleTimeoutSeconds, a.SessionMaxLifetimeSeconds);
}
