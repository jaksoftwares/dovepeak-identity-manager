using System.Diagnostics;
using System.Net;
using Dovepeak.Identity.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Dovepeak.Identity.IntegrationTests.ManagementApi;

/// <summary>
/// Milestone M6.4: Schemathesis generates requests from the published OpenAPI document — valid, boundary and malformed —
/// and sends them to the containerized Management API with a real API key. No input may produce a server error or a
/// response that contradicts the schema. Runs Schemathesis in Docker on the compose network.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Fuzz")]
[Collection(ManagementApiGroup.Name)]
public sealed class ApiFuzzingTests(ManagementApiFixture api, ITestOutputHelper output)
{
    private const string Image = "schemathesis/schemathesis:4.30.1";

    [Fact]
    public async Task Schemathesis_FindsNoServerErrorsOrSchemaViolations()
    {
        var scenario = await Scenario.CreateAsync(api);
        var scopes = new[]
        {
            "projects:read", "projects:write", "applications:read", "applications:write", "credentials:manage",
            "users:manage", "audit:read", "webhooks:manage",
        };
        var key = (await scenario.Owner.PostAsync($"{scenario.Org}/api-keys", new { name = "fuzzing", scopes }))
            .Expect(HttpStatusCode.Created).Json["key"]!.GetValue<string>();

        // Paths under this organization exercise real data; everything else proves isolation (403/404, never 500).
        // The key is expanded by Schemathesis from its environment, so it is never written to disk.
        var configDirectory = Directory.CreateTempSubdirectory("schemathesis-");
        await File.WriteAllTextAsync(Path.Combine(configDirectory.FullName, "schemathesis.toml"), $"""
            headers = {"{"} Authorization = "Bearer ${"{"}SCHEMATHESIS_API_KEY{"}"}" {"}"}

            [parameters]
            "path.orgId" = "{scenario.OrganizationId}"
            "path.projectId" = "{scenario.ProjectId}"
            "path.environmentId" = "{scenario.DevelopmentId}"
            """);

        var network = Environment.GetEnvironmentVariable("DOVEPEAK_IT_COMPOSE_NETWORK") ?? "dovepeak-identity_default";
        var maxExamples = Environment.GetEnvironmentVariable("DOVEPEAK_IT_FUZZ_EXAMPLES") ?? "25";
        var (exitCode, log) = await RunAsync("docker", key,
        [
            "run", "--rm", "--network", network,
            "-e", "SCHEMATHESIS_API_KEY",
            "-v", $"{configDirectory.FullName}:/config:ro",
            Image, "--config-file", "/config/schemathesis.toml",
            "run", "http://management-api:8080/openapi/v1.json",
            "--url", "http://management-api:8080",
            "--checks", "not_a_server_error,status_code_conformance,content_type_conformance,response_schema_conformance",
            "--max-examples", maxExamples,
            "--seed", "20261011",
            "--no-color",
        ]);
        configDirectory.Delete(recursive: true);

        output.WriteLine(log);
        Assert.True(exitCode == 0, $"Schemathesis reported failures (exit {exitCode}):\n{log}");
    }

    private static async Task<(int ExitCode, string Log)> RunAsync(string fileName, string apiKey, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // The API key travels as an environment variable ("-e NAME" copies it from here), never on a command line,
        // so process listings do not show it.
        start.Environment["SCHEMATHESIS_API_KEY"] = apiKey;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(20));
        return (process.ExitCode, await stdout + await stderr);
    }
}
