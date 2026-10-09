using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.Keycloak;

public enum ProvisioningOutcome
{
    Created,
    AlreadyExists,
}

/// <param name="Id">Keycloak's internal UUID for the client.</param>
/// <param name="Secret">Only returned when the client is first created. Never retrievable afterwards through Dovepeak (ADR-0004).</param>
public sealed record ProvisionedClient(string Id, string ClientId, ProvisioningOutcome Outcome, string? Secret);

/// <summary>
/// Typed access to the Keycloak Admin REST API for the realms the Management API service account owns.
/// Keycloak is an internal component: nothing in this class may leak into the public Dovepeak API contract.
/// </summary>
public sealed class KeycloakAdminClient(
    HttpClient httpClient,
    KeycloakAccessTokenProvider tokenProvider,
    RealmTemplate realmTemplate,
    IOptions<KeycloakOptions> options)
{
    // ---------------------------------------------------------------- Realms

    /// <summary>
    /// Creates the realm from the secure template and applies the user profile. Idempotent: re-running converges
    /// an existing realm's user profile to the template.
    /// </summary>
    public async Task<ProvisioningOutcome> CreateRealmAsync(RealmName realm, string displayName, CancellationToken cancellationToken)
    {
        var representation = realmTemplate.Build(realm, displayName, options.Value.Smtp);

        ProvisioningOutcome outcome;
        using (var response = await SendAsync(HttpMethod.Post, "admin/realms", representation, cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                outcome = ProvisioningOutcome.AlreadyExists;
            }
            else
            {
                await EnsureSuccessAsync(response, $"create realm '{realm}'").ConfigureAwait(false);
                outcome = ProvisioningOutcome.Created;

                // Rights over the new realm only appear in a newly issued admin token.
                tokenProvider.Invalidate();
            }
        }

        using var profileResponse = await SendAsync(
            HttpMethod.Put, $"admin/realms/{realm}/users/profile", realmTemplate.UserProfile(), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(profileResponse, $"apply user profile to realm '{realm}'").ConfigureAwait(false);

        return outcome;
    }

    public async Task<JsonObject?> GetRealmAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            // Forbidden means the realm exists but is not owned by this service account; treat as absent.
            return null;
        }

        await EnsureSuccessAsync(response, $"read realm '{realm}'").ConfigureAwait(false);
        return await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListRealmNamesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "admin/realms?briefRepresentation=true", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list realms").ConfigureAwait(false);

        var realms = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return realms.Select(r => r!["realm"]!.GetValue<string>()).ToList();
    }

    /// <returns>True if the realm was deleted; false if it did not exist.</returns>
    public async Task<bool> DeleteRealmAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, $"delete realm '{realm}'").ConfigureAwait(false);
        return true;
    }

    public async Task UpdateRealmAsync(RealmName realm, JsonObject changes, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}", changes, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"update realm '{realm}'").ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Client scopes (OAuth scopes)

    /// <summary>Desired representation of an application-defined OAuth scope: carried in the token's "scope" claim, no consent screen.</summary>
    public static JsonObject ClientScopeRepresentation(string name, string? description)
    {
        var scope = new JsonObject
        {
            ["name"] = name,
            ["protocol"] = "openid-connect",
            ["attributes"] = new JsonObject
            {
                ["include.in.token.scope"] = "true",
                ["display.on.consent.screen"] = "false",
            },
        };

        if (!string.IsNullOrEmpty(description))
        {
            scope["description"] = description;
        }

        return scope;
    }

    /// <summary>Creates the scope, or returns the existing one's ID (idempotent).</summary>
    public async Task<string> CreateClientScopeAsync(RealmName realm, string name, string? description, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/client-scopes",
            ClientScopeRepresentation(name, description), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (await ListClientScopesAsync(realm, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(s => s["name"]?.GetValue<string>() == name)?["id"]?.GetValue<string>()
                ?? throw new KeycloakAdminException($"Client scope '{name}' reported as existing but was not found.");
        }

        await EnsureSuccessAsync(response, $"create client scope '{name}'").ConfigureAwait(false);
        return IdFromLocation(response);
    }

    public async Task<IReadOnlyList<JsonObject>> ListClientScopesAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/client-scopes", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list client scopes").ConfigureAwait(false);
        return (await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false)).Select(s => (JsonObject)s!).ToList();
    }

    public async Task UpdateClientScopeAsync(RealmName realm, string id, JsonObject representation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/client-scopes/{id}", representation, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "update client scope").ConfigureAwait(false);
    }

    public async Task DeleteClientScopeAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/client-scopes/{id}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, "delete client scope").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Client scopes assigned to a client, by name with the scope ID as value. Optional scopes are included in a token
    /// only when requested; default scopes always are.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAssignedClientScopesAsync(
        RealmName realm, string clientId, ClientScopeAssignment assignment, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/clients/{clientId}/{Path(assignment)}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read client scopes").ConfigureAwait(false);
        return (await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s!["name"]!.GetValue<string>(), s => s!["id"]!.GetValue<string>(), StringComparer.Ordinal);
    }

    public async Task SetClientScopeAssignmentAsync(
        RealmName realm, string clientId, string scopeId, ClientScopeAssignment assignment, bool assigned, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(assigned ? HttpMethod.Put : HttpMethod.Delete,
            $"admin/realms/{realm}/clients/{clientId}/{Path(assignment)}/{scopeId}", null, cancellationToken).ConfigureAwait(false);
        if (assigned || response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, assigned ? "assign client scope" : "unassign client scope").ConfigureAwait(false);
        }
    }

    private static string Path(ClientScopeAssignment assignment) =>
        assignment == ClientScopeAssignment.Optional ? "optional-client-scopes" : "default-client-scopes";

    private static readonly string[] SmtpKeys = ["host", "port", "from", "ssl", "starttls", "auth"];

    /// <summary>
    /// Makes the realm send email through the configured SMTP server. Returns false when nothing changed or no SMTP
    /// server is configured. A realm without one cannot send verification, recovery or security alert emails.
    /// </summary>
    public async Task<bool> EnsureRealmSmtpAsync(RealmName realm, JsonObject representation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(representation);
        var smtp = options.Value.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            return false;
        }

        var desired = RealmTemplate.BuildSmtp(smtp);
        var actual = representation["smtpServer"] as JsonObject;
        var matches = actual is not null
            && SmtpKeys.All(key =>
                string.Equals(actual[key]?.ToString(), desired[key]?.ToString(), StringComparison.Ordinal));
        if (matches)
        {
            return false;
        }

        await UpdateRealmAsync(realm, new JsonObject { ["smtpServer"] = desired }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ---------------------------------------------------------------- Realm localization (tenant branding)

    /// <summary>The realm's localization overrides for a locale (texts that replace theme messages for this realm only).</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetRealmLocalizationAsync(RealmName realm, string locale, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/localization/{locale}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Dictionary<string, string>();
        }

        await EnsureSuccessAsync(response, "read realm localization").ConfigureAwait(false);
        var texts = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return texts.ToDictionary(t => t.Key, t => t.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>Sets one override, or removes it when <paramref name="value"/> is null.</summary>
    public async Task SetRealmLocalizationTextAsync(RealmName realm, string locale, string key, string? value, CancellationToken cancellationToken)
    {
        var path = $"admin/realms/{realm}/localization/{locale}/{Uri.EscapeDataString(key)}";
        if (value is null)
        {
            using var delete = await SendAsync(HttpMethod.Delete, path, null, cancellationToken).ConfigureAwait(false);
            if (delete.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(delete, $"remove localization text '{key}'").ConfigureAwait(false);
            }

            return;
        }

        using var put = await SendContentAsync(HttpMethod.Put, path, new StringContent(value, System.Text.Encoding.UTF8, "text/plain"), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(put, $"set localization text '{key}'").ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the realm's managed localization texts exactly the desired ones. Returns the keys that changed.
    /// Keys the platform does not manage are never touched.
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplyBrandingAsync(RealmName realm, TenantBranding branding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branding);
        var current = await GetRealmLocalizationAsync(realm, TenantBranding.Locale, cancellationToken).ConfigureAwait(false);
        var changed = new List<string>();
        foreach (var (key, desired) in branding.ToLocalizationTexts())
        {
            var actual = current.TryGetValue(key, out var value) ? value : null;
            if (!string.Equals(actual, desired, StringComparison.Ordinal))
            {
                await SetRealmLocalizationTextAsync(realm, TenantBranding.Locale, key, desired, cancellationToken).ConfigureAwait(false);
                changed.Add(key);
            }
        }

        return changed;
    }

    // ---------------------------------------------------------------- Client policies

    /// <summary>The realm's own client profiles (excluding Keycloak's global ones), as <c>{"profiles": [...]}</c>.</summary>
    public Task<JsonObject> GetClientProfilesAsync(RealmName realm, CancellationToken cancellationToken) =>
        GetObjectAsync($"admin/realms/{realm}/client-policies/profiles", "read client profiles", cancellationToken);

    public Task UpdateClientProfilesAsync(RealmName realm, JsonObject profiles, CancellationToken cancellationToken) =>
        PutAsync($"admin/realms/{realm}/client-policies/profiles", profiles, "update client profiles", cancellationToken);

    /// <summary>The realm's client policies, as <c>{"policies": [...]}</c>.</summary>
    public Task<JsonObject> GetClientPoliciesAsync(RealmName realm, CancellationToken cancellationToken) =>
        GetObjectAsync($"admin/realms/{realm}/client-policies/policies", "read client policies", cancellationToken);

    public Task UpdateClientPoliciesAsync(RealmName realm, JsonObject policies, CancellationToken cancellationToken) =>
        PutAsync($"admin/realms/{realm}/client-policies/policies", policies, "update client policies", cancellationToken);

    private async Task<JsonObject> GetObjectAsync(string path, string operation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation).ConfigureAwait(false);
        return await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task PutAsync(string path, JsonObject body, string operation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Clients

    public async Task<ProvisionedClient> CreateClientAsync(RealmName realm, ClientRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();

        var representation = ClientRepresentation.Build(registration);
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", representation, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var existingId = await FindClientIdAsync(realm, registration.ClientId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeycloakAdminException($"Client '{registration.ClientId}' reported as existing but was not found.");
            return new ProvisionedClient(existingId, registration.ClientId, ProvisioningOutcome.AlreadyExists, Secret: null);
        }

        await EnsureSuccessAsync(response, $"create client '{registration.ClientId}'").ConfigureAwait(false);
        var id = IdFromLocation(response);

        string? secret = null;
        if (registration.Kind is ClientKind.Confidential or ClientKind.Machine)
        {
            secret = await GetClientSecretAsync(realm, id, cancellationToken).ConfigureAwait(false);
        }

        return new ProvisionedClient(id, registration.ClientId, ProvisioningOutcome.Created, secret);
    }

    public async Task<string?> FindClientIdAsync(RealmName realm, string clientId, CancellationToken cancellationToken)
    {
        var path = $"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}&search=false";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"find client '{clientId}'").ConfigureAwait(false);

        var clients = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return clients.Count == 0 ? null : clients[0]!["id"]!.GetValue<string>();
    }

    public async Task<JsonObject> GetClientAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/clients/{id}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"read client '{id}'").ConfigureAwait(false);
        return await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies the desired configuration to an existing client, keeping its ID and secret.</summary>
    public async Task UpdateClientAsync(RealmName realm, string id, ClientRegistration registration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();

        var current = await GetClientAsync(realm, id, cancellationToken).ConfigureAwait(false);
        var desired = ClientRepresentation.Build(registration);
        foreach (var (key, value) in desired)
        {
            // Protocol mappers are managed through their own endpoint; attributes are merged below.
            if (key is "protocolMappers" or "attributes")
            {
                continue;
            }

            current[key] = value?.DeepClone();
        }

        var attributes = current["attributes"] as JsonObject ?? [];
        foreach (var (key, value) in (JsonObject)desired["attributes"]!)
        {
            attributes[key] = value?.DeepClone();
        }

        current["attributes"] = attributes;

        using var response = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/clients/{id}", current, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"update client '{registration.ClientId}'").ConfigureAwait(false);
        await SyncAudienceMappersAsync(realm, id, current, registration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteClientAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/clients/{id}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, "delete client").ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<JsonObject>> ListClientsAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/clients?max=1000", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list clients").ConfigureAwait(false);
        var clients = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return clients.Select(c => (JsonObject)c!).ToList();
    }

    private async Task SyncAudienceMappersAsync(
        RealmName realm, string id, JsonObject current, ClientRegistration registration, CancellationToken cancellationToken)
    {
        var existing = (current["protocolMappers"] as JsonArray ?? [])
            .Select(m => (JsonObject)m!)
            .Where(m => m["protocolMapper"]?.GetValue<string>() == "oidc-audience-mapper")
            .ToList();

        var wanted = registration.Audiences.ToHashSet(StringComparer.Ordinal);
        foreach (var mapper in existing)
        {
            var audience = mapper["config"]?["included.custom.audience"]?.GetValue<string>();
            if (audience is null || !wanted.Remove(audience))
            {
                using var delete = await SendAsync(HttpMethod.Delete,
                    $"admin/realms/{realm}/clients/{id}/protocol-mappers/models/{mapper["id"]!.GetValue<string>()}", null, cancellationToken).ConfigureAwait(false);
                await EnsureSuccessAsync(delete, "remove audience mapper").ConfigureAwait(false);
            }
        }

        foreach (var audience in wanted)
        {
            using var add = await SendAsync(HttpMethod.Post,
                $"admin/realms/{realm}/clients/{id}/protocol-mappers/models", ClientRepresentation.AudienceMapper(audience), cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(add, "add audience mapper").ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- Client roles

    public async Task CreateClientRoleAsync(RealmName realm, string clientId, string name, string? description, CancellationToken cancellationToken)
    {
        var role = new JsonObject { ["name"] = name, ["description"] = description };
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients/{clientId}/roles", role, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        await EnsureSuccessAsync(response, $"create role '{name}'").ConfigureAwait(false);
    }

    public async Task DeleteClientRoleAsync(RealmName realm, string clientId, string name, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            $"admin/realms/{realm}/clients/{clientId}/roles/{Uri.EscapeDataString(name)}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, $"delete role '{name}'").ConfigureAwait(false);
        }
    }

    public async Task SetClientRoleAssignmentAsync(
        RealmName realm, string userId, string clientId, string roleName, bool assigned, CancellationToken cancellationToken)
    {
        using var get = await SendAsync(HttpMethod.Get,
            $"admin/realms/{realm}/clients/{clientId}/roles/{Uri.EscapeDataString(roleName)}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(get, $"read role '{roleName}'").ConfigureAwait(false);
        var role = await ReadObjectAsync(get, cancellationToken).ConfigureAwait(false);

        using var change = await SendAsync(assigned ? HttpMethod.Post : HttpMethod.Delete,
            $"admin/realms/{realm}/users/{userId}/role-mappings/clients/{clientId}", new JsonArray(role), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(change, assigned ? "assign role" : "remove role").ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetUserClientRolesAsync(RealmName realm, string userId, string clientId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"admin/realms/{realm}/users/{userId}/role-mappings/clients/{clientId}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read user roles").ConfigureAwait(false);
        var roles = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return roles.Select(r => r!["name"]!.GetValue<string>()).ToList();
    }

    // ---------------------------------------------------------------- User lookup

    public async Task<IReadOnlyList<JsonObject>> SearchUsersAsync(RealmName realm, string? email, int first, int max, CancellationToken cancellationToken)
    {
        var query = email is null
            ? string.Create(CultureInfo.InvariantCulture, $"first={first}&max={max}")
            : string.Create(CultureInfo.InvariantCulture, $"email={Uri.EscapeDataString(email)}&exact=true&first={first}&max={max}");
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/users?{query}&briefRepresentation=true", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "search users").ConfigureAwait(false);
        var users = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return users.Select(u => (JsonObject)u!).ToList();
    }

    public async Task<JsonObject?> GetUserAsync(RealmName realm, string userId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/users/{Uri.EscapeDataString(userId)}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "read user").ConfigureAwait(false);
        return await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates a new client secret, invalidating the previous one immediately.</summary>
    public async Task<string> RegenerateClientSecretAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients/{id}/client-secret", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "regenerate client secret").ConfigureAwait(false);
        var body = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return body["value"]!.GetValue<string>();
    }

    /// <summary>
    /// When the previous secret stops being accepted, or null if there is none. With the realm's secret-rotation policy
    /// a regenerated secret keeps the old one valid for the overlap period (ADR-0004).
    /// </summary>
    public async Task<DateTimeOffset?> GetPreviousSecretExpiryAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var rotated = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/clients/{id}/client-secret/rotated", null, cancellationToken).ConfigureAwait(false);
        if (rotated.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(rotated, "read rotated client secret").ConfigureAwait(false);
        var client = await GetClientAsync(realm, id, cancellationToken).ConfigureAwait(false);
        var expiry = client["attributes"]?["client.secret.rotated.expiration.time"]?.GetValue<string>();
        return long.TryParse(expiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>Stops the previous secret from being accepted immediately (for example after a leak).</summary>
    public async Task RevokePreviousSecretAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/clients/{id}/client-secret/rotated", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, "revoke previous client secret").ConfigureAwait(false);
        }
    }

    private async Task<string> GetClientSecretAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/clients/{id}/client-secret", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read client secret").ConfigureAwait(false);
        var body = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return body["value"]!.GetValue<string>();
    }

    // ---------------------------------------------------------------- Users and sessions

    /// <returns>The new user's ID.</returns>
    public async Task<string> CreateUserAsync(RealmName realm, string email, string password, bool emailVerified, CancellationToken cancellationToken)
    {
        var representation = new JsonObject
        {
            ["username"] = email,
            ["email"] = email,
            ["enabled"] = true,
            ["emailVerified"] = emailVerified,
            ["credentials"] = new JsonArray(new JsonObject
            {
                ["type"] = "password",
                ["value"] = password,
                ["temporary"] = false,
            }),
        };

        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", representation, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "create user").ConfigureAwait(false);
        return IdFromLocation(response);
    }

    public async Task<IReadOnlyList<JsonObject>> GetUserSessionsAsync(RealmName realm, string userId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/users/{userId}/sessions", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list user sessions").ConfigureAwait(false);
        var sessions = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return sessions.Select(s => (JsonObject)s!).ToList();
    }

    /// <summary>Revokes every session (and therefore every refresh token) for the user.</summary>
    public async Task LogoutUserAsync(RealmName realm, string userId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users/{userId}/logout", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "log out user").ConfigureAwait(false);
    }

    /// <summary>
    /// Revokes every session in the realm and rejects all tokens issued before now (Keycloak "not-before" push).
    /// </summary>
    public async Task LogoutAllAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/logout-all", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "log out all sessions").ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(RealmName realm, string sessionId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/sessions/{sessionId}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "delete session").ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Signing keys

    /// <summary>Returns the realm's key metadata (public material only).</summary>
    public async Task<JsonObject> GetKeysAsync(RealmName realm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/keys", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read realm keys").ConfigureAwait(false);
        return await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JsonObject>> GetKeyProvidersAsync(RealmName realm, CancellationToken cancellationToken)
    {
        var path = $"admin/realms/{realm}/components?type={Uri.EscapeDataString("org.keycloak.keys.KeyProvider")}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "list key providers").ConfigureAwait(false);
        var providers = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return providers.Select(p => (JsonObject)p!).ToList();
    }

    /// <summary>Adds a generated key provider (for example "rsa-generated" or "hmac-generated").</summary>
    /// <returns>The new key provider component's ID.</returns>
    public async Task<string> AddGeneratedKeyAsync(
        RealmName realm, string providerId, string algorithm, string name, long priority, CancellationToken cancellationToken)
    {
        var realmRepresentation = await GetRealmAsync(realm, cancellationToken).ConfigureAwait(false)
            ?? throw new KeycloakAdminException($"Realm '{realm}' not found.");

        var config = new JsonObject
        {
            ["priority"] = new JsonArray(priority.ToString(CultureInfo.InvariantCulture)),
            ["enabled"] = new JsonArray("true"),
            ["active"] = new JsonArray("true"),
            ["algorithm"] = new JsonArray(algorithm),
        };

        if (providerId == "rsa-generated")
        {
            config["keySize"] = new JsonArray("2048");
        }
        else if (providerId == "hmac-generated")
        {
            config["secretSize"] = new JsonArray("64");
        }

        var component = new JsonObject
        {
            ["name"] = name,
            ["providerId"] = providerId,
            ["providerType"] = "org.keycloak.keys.KeyProvider",
            ["parentId"] = realmRepresentation["id"]!.GetValue<string>(),
            ["config"] = config,
        };

        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/components", component, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"add {providerId} key").ConfigureAwait(false);
        return IdFromLocation(response);
    }

    /// <summary>
    /// Sets a key provider's state. Active keys sign new tokens; enabled-but-passive keys only verify
    /// existing tokens; disabled keys are unusable.
    /// </summary>
    public async Task SetKeyProviderStateAsync(RealmName realm, string componentId, bool active, bool enabled, CancellationToken cancellationToken)
    {
        using var get = await SendAsync(HttpMethod.Get, $"admin/realms/{realm}/components/{componentId}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(get, "read key provider").ConfigureAwait(false);
        var component = await ReadObjectAsync(get, cancellationToken).ConfigureAwait(false);

        var config = (JsonObject)component["config"]!;
        config["active"] = new JsonArray(active ? "true" : "false");
        config["enabled"] = new JsonArray(enabled ? "true" : "false");

        using var put = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/components/{componentId}", component, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(put, "update key provider").ConfigureAwait(false);
    }

    public async Task UpdateComponentAsync(RealmName realm, string componentId, JsonObject component, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, $"admin/realms/{realm}/components/{componentId}", component, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "update component").ConfigureAwait(false);
    }

    public async Task DeleteComponentAsync(RealmName realm, string componentId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}/components/{componentId}", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "delete component").ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Events

    /// <summary>Returns authentication events, newest first.</summary>
    public async Task<IReadOnlyList<JsonObject>> GetEventsAsync(RealmName realm, int first, int max, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"admin/realms/{realm}/events?first={first}&max={max}");
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read events").ConfigureAwait(false);
        var events = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return events.Select(e => (JsonObject)e!).ToList();
    }

    /// <summary>Returns administrative events, newest first.</summary>
    public async Task<IReadOnlyList<JsonObject>> GetAdminEventsAsync(RealmName realm, int first, int max, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"admin/realms/{realm}/admin-events?first={first}&max={max}");
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "read admin events").ConfigureAwait(false);
        var events = await ReadArrayAsync(response, cancellationToken).ConfigureAwait(false);
        return events.Select(e => (JsonObject)e!).ToList();
    }

    // ---------------------------------------------------------------- Plumbing

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken) =>
        SendContentAsync(method, path, body is null ? null : JsonContent.Create(body), cancellationToken);

    private async Task<HttpResponseMessage> SendContentAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;

        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Keycloak error bodies describe validation failures (e.g. invalid password policy) and are safe to surface
        // to operators. They are truncated to keep logs bounded.
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var detail = body.Length > 500 ? body[..500] : body;
        throw new KeycloakAdminException($"Keycloak failed to {operation}: {detail}", response.StatusCode);
    }

    private static string IdFromLocation(HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new KeycloakAdminException("Keycloak did not return a Location header for the created resource.");
        return location.Segments[^1].TrimEnd('/');
    }

    private static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false)
        ?? throw new KeycloakAdminException("Keycloak returned an empty response.");

    private static async Task<JsonArray> ReadArrayAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken).ConfigureAwait(false)
        ?? throw new KeycloakAdminException("Keycloak returned an empty response.");
}
