using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dovepeak.Identity.DevTool;

/// <summary>
/// Incident drill for milestone M6.1 (docs/operations/observability.md#drills): tampers with a tenant client directly
/// in the identity engine, as an attacker with admin access would, then waits until reconciliation reverts it and the
/// ConfigurationDriftCorrected alert fires.
/// </summary>
internal static class DriftDrill
{
    // ".invalid" is reserved (RFC 2606): the planted redirect URI can never resolve.
    private const string PlantedRedirectUri = "https://drift-drill.invalid/callback";

    public static async Task<int> RunAsync(IServiceProvider services, CommandArgs args)
    {
        var realm = RealmName.Parse(args.Require("realm"));
        var clientId = args.Require("client");
        var prometheus = new Uri(args.Get("prometheus") ?? "http://localhost:9090/");
        var timeout = TimeSpan.FromMinutes(double.Parse(args.Get("timeout-minutes") ?? "10", System.Globalization.CultureInfo.InvariantCulture));

        var admin = services.GetRequiredService<KeycloakAdminClient>();
        var client = (await admin.ListClientsAsync(realm, CancellationToken.None))
            .SingleOrDefault(c => c["clientId"]?.GetValue<string>() == clientId)
            ?? throw new ArgumentException($"Client '{clientId}' not found in realm {realm}.");
        var id = client["id"]!.GetValue<string>();

        using (var keycloak = await KeycloakHttpAsync(services))
        {
            var representation = (await keycloak.GetFromJsonAsync<JsonObject>($"admin/realms/{realm}/clients/{id}"))!;
            ((JsonArray)representation["redirectUris"]!).Add(PlantedRedirectUri);
            (await keycloak.PutAsJsonAsync($"admin/realms/{realm}/clients/{id}", representation)).EnsureSuccessStatusCode();
        }

        var started = Stopwatch.StartNew();
        Console.WriteLine($"{DateTimeOffset.UtcNow:u}  Planted redirect URI {PlantedRedirectUri} on {clientId} in {realm}.");
        Console.WriteLine($"Waiting up to {timeout.TotalMinutes:0} minutes for reconciliation and the ConfigurationDriftCorrected alert...");

        using var http = new HttpClient { BaseAddress = prometheus };
        bool reverted = false, alerted = false;
        while (started.Elapsed < timeout && !(reverted && alerted))
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (!reverted)
            {
                // Service-account tokens are short-lived: get a fresh one for each check.
                using var fresh = await KeycloakHttpAsync(services);
                var current = (await fresh.GetFromJsonAsync<JsonObject>($"admin/realms/{realm}/clients/{id}"))!;
                reverted = !((JsonArray)current["redirectUris"]!).Any(u => u?.GetValue<string>() == PlantedRedirectUri);
                if (reverted)
                {
                    Console.WriteLine($"{DateTimeOffset.UtcNow:u}  Reverted by reconciliation after {started.Elapsed:mm\\:ss}.");
                }
            }

            if (!alerted)
            {
                var alerts = await http.GetFromJsonAsync<JsonObject>("api/v1/alerts");
                alerted = alerts?["data"]?["alerts"] is JsonArray list && list.Any(a =>
                    a?["labels"]?["alertname"]?.GetValue<string>() == "ConfigurationDriftCorrected"
                    && a["labels"]?["kind"]?.GetValue<string>() == "client_config_restored"
                    && a["state"]?.GetValue<string>() == "firing");
                if (alerted)
                {
                    Console.WriteLine($"{DateTimeOffset.UtcNow:u}  Alert ConfigurationDriftCorrected (client_config_restored) firing after {started.Elapsed:mm\\:ss}.");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Trace (Grafana > Explore > Tempo): {{ name = \"reconcile environment\" && span.dovepeak.realm = \"{realm}\" }}");
        Console.WriteLine($"Audit: GET /v1/organizations/{{orgId}}/audit-events?type=drift.corrected");
        Console.WriteLine(reverted && alerted ? "DRILL PASSED" : $"DRILL FAILED (reverted: {reverted}, alerted: {alerted})");
        return reverted && alerted ? 0 : 1;
    }

    private static async Task<HttpClient> KeycloakHttpAsync(IServiceProvider services)
    {
        var token = await services.GetRequiredService<KeycloakAccessTokenProvider>().GetTokenAsync(CancellationToken.None);
        var http = new HttpClient { BaseAddress = services.GetRequiredService<IOptions<KeycloakOptions>>().Value.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
