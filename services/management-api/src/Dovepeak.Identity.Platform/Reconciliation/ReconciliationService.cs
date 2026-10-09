using System.Text.Json.Nodes;
using Dovepeak.Identity.Keycloak;
using Dovepeak.Identity.Persistence;
using Dovepeak.Identity.Persistence.Tenancy;
using Dovepeak.Identity.Platform.Applications;
using Dovepeak.Identity.Platform.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dovepeak.Identity.Platform.Reconciliation;

public sealed record DriftCorrection(Guid OrganizationId, string RealmName, string Kind, string Subject);

/// <summary>
/// Converges the identity engine to the platform's desired state (milestone M3.7, threat model T-01).
/// The platform database is the source of truth for configuration; changes made directly in the engine are reverted
/// and every correction is audited.
/// </summary>
public sealed partial class ReconciliationService(IServiceScopeFactory scopeFactory, ILogger<ReconciliationService> logger)
{
    /// <summary>Clients Keycloak creates in every realm; never treated as drift.</summary>
    private static readonly HashSet<string> BuiltInClients = new(StringComparer.Ordinal)
    {
        "account", "account-console", "admin-cli", "broker", "realm-management", "security-admin-console",
    };

    public async Task<IReadOnlyList<DriftCorrection>> ReconcileAllAsync(CancellationToken ct)
    {
        List<Guid> environmentIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            db.TenantScope.EnterSystem();
            environmentIds = await db.Environments.Where(e => e.State == ProvisioningState.Ready).Select(e => e.Id).ToListAsync(ct);
        }

        var corrections = new List<DriftCorrection>();
        foreach (var id in environmentIds)
        {
            try
            {
                corrections.AddRange(await ReconcileEnvironmentAsync(id, ct));
            }
            catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException)
            {
                LogEnvironmentFailed(logger, ex, id);
            }
        }

        return corrections;
    }

    public async Task<IReadOnlyList<DriftCorrection>> ReconcileEnvironmentAsync(Guid environmentId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<PlatformDbContext>();
        var engine = services.GetRequiredService<KeycloakAdminClient>();
        var template = services.GetRequiredService<RealmTemplate>();
        var audit = services.GetRequiredService<ManagementAuditLog>();
        db.TenantScope.EnterSystem();

        var environment = await db.Environments.SingleAsync(e => e.Id == environmentId, ct);
        var realm = RealmName.Parse(environment.RealmName);
        var corrections = new List<DriftCorrection>();
        void Corrected(string kind, string subject, object? details = null)
        {
            corrections.Add(new DriftCorrection(environment.OrganizationId, realm.Value, kind, subject));
            audit.Record(environment.OrganizationId, "drift.corrected", "environment", environment.Id, new { kind, subject, details });
            LogDrift(logger, kind, subject, realm.Value);
        }

        // 1. The realm must exist and carry the security baseline.
        var representation = await engine.GetRealmAsync(realm, ct);
        if (representation is null)
        {
            var project = await db.Projects.SingleAsync(p => p.Id == environment.ProjectId, ct);
            await engine.CreateRealmAsync(realm, project.Name, ct);
            Corrected("realm_recreated", realm.Value);
            representation = await engine.GetRealmAsync(realm, ct);
        }

        var baselineDrift = ScalarDrift(template.Baseline(), representation!);
        baselineDrift.AddRange(BaselineArrays.Where(key =>
            !SetEquals(representation![key], ((JsonArray)template.Baseline()[key]!).Select(n => n!.GetValue<string>()))));
        if (baselineDrift.Count > 0)
        {
            var restore = new JsonObject();
            foreach (var key in baselineDrift)
            {
                restore[key] = template.Baseline()[key]!.DeepClone();
            }

            await engine.UpdateRealmAsync(realm, restore, ct);
            Corrected("realm_settings_restored", string.Join(",", baselineDrift));
        }

        // 1b. Client policies (PKCE, rejected grants, secret rotation) must match the template exactly. Repairs never pass
        //     through a state with weaker enforcement: missing profiles are added before policies are restored, and
        //     unknown profiles are removed only once no policy references them.
        var desiredProfiles = (JsonArray)template.Baseline()["clientProfiles"]!["profiles"]!;
        var desiredPolicies = (JsonArray)template.Baseline()["clientPolicies"]!["policies"]!;
        var actualProfiles = (await engine.GetClientProfilesAsync(realm, ct))["profiles"] as JsonArray ?? [];
        var actualPolicies = (await engine.GetClientPoliciesAsync(realm, ct))["policies"] as JsonArray ?? [];
        if (!IsSubset(desiredProfiles, actualProfiles) || !IsSubset(desiredPolicies, actualPolicies))
        {
            var desiredNames = desiredProfiles.Select(p => p!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var union = new JsonArray([.. desiredProfiles.Select(p => p!.DeepClone()),
                .. actualProfiles.Where(p => !desiredNames.Contains(p!["name"]!.GetValue<string>())).Select(p => p!.DeepClone())]);
            await engine.UpdateClientProfilesAsync(realm, new JsonObject { ["profiles"] = union }, ct);
            await engine.UpdateClientPoliciesAsync(realm, new JsonObject { ["policies"] = desiredPolicies.DeepClone() }, ct);
            await engine.UpdateClientProfilesAsync(realm, new JsonObject { ["profiles"] = desiredProfiles.DeepClone() }, ct);
            Corrected("client_policies_restored", realm.Value);
        }

        // 1b'. Hosted pages and emails carry the project's branding and email templates, nothing else.
        var brandingProject = await db.Projects.SingleAsync(p => p.Id == environment.ProjectId, ct);
        var brandingChanges = await engine.ApplyBrandingAsync(realm, Projects.BrandingService.ForProject(brandingProject), ct);
        if (brandingChanges.Count > 0)
        {
            Corrected("branding_restored", realm.Value, brandingChanges);
        }

        // 1c. Environment scopes must exist in the engine and stay in the "scope" claim.
        var scopes = await db.EnvironmentScopes.Where(s => s.EnvironmentId == environmentId).ToListAsync(ct);
        var engineScopes = (await engine.ListClientScopesAsync(realm, ct)).ToDictionary(s => s["id"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var environmentScope in scopes)
        {
            if (environmentScope.EngineScopeId is null || !engineScopes.TryGetValue(environmentScope.EngineScopeId, out var actualScope))
            {
                environmentScope.EngineScopeId = await engine.CreateClientScopeAsync(realm, environmentScope.Name, environmentScope.Description, ct);
                Corrected("scope_recreated", environmentScope.Name);
                continue;
            }

            var desiredScope = KeycloakAdminClient.ClientScopeRepresentation(environmentScope.Name, environmentScope.Description);
            if (!IsSubset(desiredScope, actualScope))
            {
                desiredScope["id"] = environmentScope.EngineScopeId;
                await engine.UpdateClientScopeAsync(realm, environmentScope.EngineScopeId, desiredScope, ct);
                Corrected("scope_restored", environmentScope.Name);
            }
        }

        // 2. Every application must exist with exactly its desired configuration.
        var applications = await db.Applications.Where(a => a.EnvironmentId == environmentId).ToListAsync(ct);
        var engineClients = await engine.ListClientsAsync(realm, ct);
        var byClientId = engineClients.ToDictionary(c => c["clientId"]!.GetValue<string>(), StringComparer.Ordinal);

        foreach (var application in applications)
        {
            var registration = ApplicationService.ToRegistration(application);
            if (!byClientId.TryGetValue(application.ClientId, out var client))
            {
                // Recreated clients receive a new secret; the owner must rotate it to obtain one (documented).
                var created = await engine.CreateClientAsync(realm, registration, ct);
                application.EngineClientId = created.Id;
                Corrected("client_recreated", application.ClientId);
            }
            else
            {
                application.EngineClientId = client["id"]!.GetValue<string>();
                var differences = ClientDrift(client, registration);
                if (differences.Count > 0)
                {
                    await engine.UpdateClientAsync(realm, application.EngineClientId, registration, ct);
                    Corrected("client_config_restored", application.ClientId, differences);
                }
            }

            var scopeChanges = await ScopeService.SyncClientScopesAsync(engine, realm, application.EngineClientId, application.Scopes, scopes, ct);
            if (scopeChanges.Count > 0)
            {
                Corrected("client_scopes_restored", application.ClientId, scopeChanges);
            }
        }

        // 3. Clients unknown to the platform were created outside it and are removed.
        var known = applications.Select(a => a.ClientId).ToHashSet(StringComparer.Ordinal);
        foreach (var (clientId, client) in byClientId)
        {
            if (!known.Contains(clientId) && !BuiltInClients.Contains(clientId))
            {
                await engine.DeleteClientAsync(realm, client["id"]!.GetValue<string>(), ct);
                Corrected("unknown_client_removed", clientId);
            }
        }

        await db.SaveChangesAsync(ct);
        return corrections;
    }

    /// <summary>
    /// True when every value in <paramref name="desired"/> is present in <paramref name="actual"/>. Arrays must have the
    /// same length (no extra or missing items); objects may carry extra keys that Keycloak adds itself.
    /// </summary>
    private static bool IsSubset(JsonNode? desired, JsonNode? actual) => desired switch
    {
        null => true,
        JsonObject o => actual is JsonObject a && o.All(p => a.ContainsKey(p.Key) && IsSubset(p.Value, a[p.Key])),
        JsonArray d => actual is JsonArray a && d.Count == a.Count && d.Select((item, i) => IsSubset(item, a[i])).All(x => x),
        _ => actual is JsonValue && actual.ToJsonString() == desired.ToJsonString(),
    };

    /// <summary>Security-relevant list settings: alert emails and the event types the audit pipeline collects.</summary>
    private static readonly string[] BaselineArrays = ["eventsListeners", "enabledEventTypes"];

    private static List<string> ScalarDrift(JsonObject baseline, JsonObject actual) =>
        baseline.Where(p => p.Value is JsonValue && p.Key != "attributes" && actual[p.Key] is { } value
                && value.ToJsonString() != p.Value!.ToJsonString())
            .Select(p => p.Key).ToList();

    /// <summary>Security-relevant client settings compared against the desired registration.</summary>
    private static List<string> ClientDrift(JsonObject actual, ClientRegistration desired)
    {
        var differences = new List<string>();
        var interactive = desired.Kind != ClientKind.Machine;

        void Check(string name, bool condition)
        {
            if (!condition)
            {
                differences.Add(name);
            }
        }

        Check("redirectUris", SetEquals(actual["redirectUris"], desired.RedirectUris.Select(u => u.OriginalString)));
        Check("webOrigins", SetEquals(actual["webOrigins"], desired.WebOrigins.Select(RedirectUriPolicy.ToOriginString)));
        Check("publicClient", actual["publicClient"]?.GetValue<bool>() == (desired.Kind == ClientKind.Public));
        Check("standardFlowEnabled", actual["standardFlowEnabled"]?.GetValue<bool>() == interactive);
        Check("implicitFlowEnabled", actual["implicitFlowEnabled"]?.GetValue<bool>() == false);
        Check("directAccessGrantsEnabled", actual["directAccessGrantsEnabled"]?.GetValue<bool>() == false);
        Check("serviceAccountsEnabled", actual["serviceAccountsEnabled"]?.GetValue<bool>() == (desired.Kind == ClientKind.Machine));
        Check("fullScopeAllowed", actual["fullScopeAllowed"]?.GetValue<bool>() == false);
        Check("enabled", actual["enabled"]?.GetValue<bool>() == true);
        Check("pkce", actual["attributes"]?["pkce.code.challenge.method"]?.GetValue<string>() == "S256");
        foreach (var (attribute, value) in desired.TokenPolicy.ToAttributes())
        {
            // Keycloak treats a missing attribute and an empty one alike: both inherit the realm setting.
            Check(attribute, (actual["attributes"]?[attribute]?.GetValue<string>() ?? string.Empty) == value);
        }

        return differences;
    }

    private static bool SetEquals(JsonNode? actual, IEnumerable<string> desired) =>
        (actual as JsonArray ?? []).Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(desired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift corrected: {Kind} {Subject} in realm {Realm}")]
    private static partial void LogDrift(ILogger logger, string kind, string subject, string realm);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation failed for environment {EnvironmentId}")]
    private static partial void LogEnvironmentFailed(ILogger logger, Exception exception, Guid environmentId);
}
