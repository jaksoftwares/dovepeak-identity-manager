# Dovepeak.Identity

ASP.NET Core integration for [Dovepeak Identity](https://github.com/jaksoftwares/dovepeak-identity-manager).

## Protect an API (one line)

```csharp
builder.Services.AddDovepeakAuthentication(builder.Configuration.GetSection("Dovepeak"));

app.MapGet("/orders", () => ...).RequireScope("orders:read");
app.MapDelete("/orders/{id}", (string id) => ...).RequireDovepeakRole("administrator");
```

```json
{ "Dovepeak": { "Issuer": "https://id.example.com/realms/dp-…", "Audience": "orders-api" } }
```

Validates signature (RS256 only), issuer, audience and expiry. Roles come from the `roles` claim and work with
`RequireRole` and `User.IsInRole`; scopes come from the `scope` claim. Set `Introspection:Enabled` (with the API's
client credentials) to reject revoked sessions immediately.

## Automate with the Management API

```csharp
builder.Services.AddDovepeakManagementClient(builder.Configuration.GetSection("DovepeakManagement")); // BaseUrl, ApiKey

var project = await management.CreateProjectAsync(orgId, "shop", "Shop");
project = await management.WaitUntilReadyAsync(orgId, project.Id);
```

Errors are `DovepeakApiException` with a stable `Code`. Create requests are idempotent.
