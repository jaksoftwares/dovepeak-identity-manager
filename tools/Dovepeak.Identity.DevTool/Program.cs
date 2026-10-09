using Dovepeak.Identity.DevTool;
using Dovepeak.Identity.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// dovepeak-dev — local development and operations tooling for Dovepeak Identity.
// Connects to Keycloak with the Management API's least-privilege service account.

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Keycloak:BaseUrl"] = "http://localhost:8081/",
        ["Keycloak:ClientId"] = "dovepeak-management",
        ["Keycloak:ClientSecret"] = "management_client_local_only",
        ["Keycloak:Smtp:Host"] = "mailpit",
        ["Keycloak:Smtp:Port"] = "1025",
        ["Keycloak:Smtp:StartTls"] = "false",
        ["PublicUrl"] = "http://localhost:8080/",
    })
    .AddEnvironmentVariables(prefix: "DOVEPEAK_")
    .Build();

var services = new ServiceCollection();
services.AddKeycloakAdmin(configuration);
await using var provider = services.BuildServiceProvider();

var commandArgs = new CommandArgs(args.Skip(1).ToArray());
var publicUrl = new Uri(configuration["PublicUrl"]!);

try
{
    return args.FirstOrDefault() switch
    {
        "demo-setup" => await DemoSetup.RunAsync(provider, publicUrl, commandArgs),
        "scale-test" => await ScaleTest.RunAsync(provider, publicUrl, commandArgs),
        "delete-scale-realms" => await ScaleTest.DeleteAllAsync(provider),
        "rotate-keys" => await KeyCommands.RotateAsync(provider, commandArgs),
        "retire-keys" => await KeyCommands.RetireAsync(provider, commandArgs),
        "emergency-rotate-keys" => await KeyCommands.EmergencyRotateAsync(provider, commandArgs),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is KeycloakAdminException or ArgumentException or HttpRequestException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("""
        dovepeak-dev <command> [options]

        Commands:
          demo-setup              Provision the demo realm, BFF client and user; write examples/nextjs-bff/.env.local
              --app-url <url>     Demo app URL (default http://localhost:3000)
          scale-test              Measure provisioning and runtime behaviour as realm count grows (M1.6)
              --realms <n>        Number of realms to create (default 100)
              --keep              Do not delete the realms afterwards
          delete-scale-realms     Delete every realm left behind by scale-test --keep
          rotate-keys             Planned signing key rotation
              --realm <name>
          retire-keys             Delete keys that have been passive for at least --min-age
              --realm <name>
              --min-age <hh:mm:ss>   (default 00:15:00)
          emergency-rotate-keys   Replace all keys and revoke every session (key compromise)
              --realm <name>
              --confirm           Required

        Environment overrides use the DOVEPEAK_ prefix, e.g. DOVEPEAK_Keycloak__BaseUrl, DOVEPEAK_PublicUrl.
        """);
    return 2;
}
