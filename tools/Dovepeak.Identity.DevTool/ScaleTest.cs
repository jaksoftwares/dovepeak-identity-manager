using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.DependencyInjection;

namespace Dovepeak.Identity.DevTool;

/// <summary>
/// Milestone M1.6 — measures how Keycloak behaves as the number of tenant realms grows:
/// provisioning time, token endpoint and discovery latency (through the public edge), and the size of the
/// Management API service account's admin token.
/// </summary>
internal static class ScaleTest
{
    private const string Prefix = "scale-";

    public static async Task<int> RunAsync(IServiceProvider services, Uri publicUrl, CommandArgs args)
    {
        var admin = services.GetRequiredService<KeycloakAdminClient>();
        var tokens = services.GetRequiredService<KeycloakAccessTokenProvider>();
        var total = int.Parse(args.Get("realms") ?? "100", CultureInfo.InvariantCulture);
        var checkpoints = Enumerable.Range(1, 10).Select(i => Math.Max(1, total * i / 10)).Distinct().ToHashSet();
        var ct = CancellationToken.None;
        var run = Guid.NewGuid().ToString("N")[..6];

        using var http = new HttpClient { BaseAddress = publicUrl, Timeout = TimeSpan.FromSeconds(30) };
        var created = new List<(RealmName Realm, string Secret)>();
        var provisioning = new List<double>();
        var rows = new List<string>();

        Console.WriteLine($"Provisioning {total} realms (run {run})...");
        for (var i = 1; i <= total; i++)
        {
            var realm = RealmName.Parse($"{Prefix}{run}-{i:D4}");
            var watch = Stopwatch.StartNew();
            await admin.CreateRealmAsync(realm, $"Scale test {i}", ct);
            var client = await admin.CreateClientAsync(realm, new ClientRegistration("scale-service", ClientKind.Machine), ct);
            provisioning.Add(watch.Elapsed.TotalMilliseconds);
            created.Add((realm, client.Secret!));

            if (checkpoints.Contains(i))
            {
                tokens.Invalidate();
                var adminTokenBytes = (await tokens.GetTokenAsync(ct)).Length;
                var tokenLatency = await SampleAsync(created, r => RequestTokenAsync(http, r.Realm, r.Secret));
                var discoveryLatency = await SampleAsync(created, r => DiscoveryAsync(http, r.Realm));
                var recent = provisioning.TakeLast(Math.Max(1, total / 10)).ToList();

                var row = string.Create(CultureInfo.InvariantCulture,
                    $"| {i,5} | {Percentile(recent, 50),8:F0} | {Percentile(recent, 95),8:F0} | {Percentile(tokenLatency, 50),8:F0} | {Percentile(tokenLatency, 95),8:F0} | {Percentile(discoveryLatency, 95),9:F0} | {adminTokenBytes,10} |");
                rows.Add(row);
                Console.WriteLine(row);
            }
        }

        Console.WriteLine($"""

            ## Scale test results ({total} realms, {DateTime.UtcNow:yyyy-MM-dd})

            | Realms | Provision p50 ms | Provision p95 ms | Token p50 ms | Token p95 ms | Discovery p95 ms | Admin token bytes |
            | -----: | ---------------: | ---------------: | -----------: | -----------: | ---------------: | ----------------: |
            {string.Join(Environment.NewLine, rows)}
            """);

        if (!args.Has("keep"))
        {
            Console.WriteLine($"\nDeleting {created.Count} realms...");
            foreach (var (realm, _) in created)
            {
                await admin.DeleteRealmAsync(realm, ct);
            }
        }

        return 0;
    }

    public static async Task<int> DeleteAllAsync(IServiceProvider services)
    {
        var admin = services.GetRequiredService<KeycloakAdminClient>();
        var realms = (await admin.ListRealmNamesAsync(CancellationToken.None)).Where(r => r.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        Console.WriteLine($"Deleting {realms.Count} scale-test realms...");
        foreach (var realm in realms)
        {
            await admin.DeleteRealmAsync(RealmName.Parse(realm), CancellationToken.None);
        }

        return 0;
    }

    /// <summary>Times one operation against up to 20 realms spread across everything created so far.</summary>
    private static async Task<List<double>> SampleAsync<T>(List<T> items, Func<T, Task> operation)
    {
        var step = Math.Max(1, items.Count / 20);
        var timings = new List<double>();
        for (var index = 0; index < items.Count; index += step)
        {
            var watch = Stopwatch.StartNew();
            await operation(items[index]);
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        return timings;
    }

    private static async Task RequestTokenAsync(HttpClient http, RealmName realm, string secret)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "scale-service",
            ["client_secret"] = secret,
        });
        using var response = await http.PostAsync(new Uri($"realms/{realm}/protocol/openid-connect/token", UriKind.Relative), form);
        response.EnsureSuccessStatusCode();
    }

    private static async Task DiscoveryAsync(HttpClient http, RealmName realm)
    {
        var document = await http.GetFromJsonAsync<JsonObject>(new Uri($"realms/{realm}/.well-known/openid-configuration", UriKind.Relative));
        ArgumentNullException.ThrowIfNull(document);
    }

    private static double Percentile(List<double> values, int percentile)
    {
        var sorted = values.Order().ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
