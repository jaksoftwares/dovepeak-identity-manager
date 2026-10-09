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

    /// <summary>Generates a new client secret, invalidating the previous one immediately.</summary>
    public async Task<string> RegenerateClientSecretAsync(RealmName realm, string id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients/{id}/client-secret", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "regenerate client secret").ConfigureAwait(false);
        var body = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return body["value"]!.GetValue<string>();
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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

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
