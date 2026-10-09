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
                continue;
            }

            application.EngineClientId = client["id"]!.GetValue<string>();
            var differences = ClientDrift(client, registration);
            if (differences.Count > 0)
            {
                await engine.UpdateClientAsync(realm, application.EngineClientId, registration, ct);
                Corrected("client_config_restored", application.ClientId, differences);
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
        return differences;
    }

    private static bool SetEquals(JsonNode? actual, IEnumerable<string> desired) =>
        (actual as JsonArray ?? []).Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(desired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift corrected: {Kind} {Subject} in realm {Realm}")]
    private static partial void LogDrift(ILogger logger, string kind, string subject, string realm);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation failed for environment {EnvironmentId}")]
    private static partial void LogEnvironmentFailed(ILogger logger, Exception exception, Guid environmentId);
}
