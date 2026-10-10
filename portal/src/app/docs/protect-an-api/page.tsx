export const metadata = { title: "Protect an API" };

export default function ProtectApi() {
  return (
    <article>
      <h1>Protect an API</h1>
      <p>
        Resource servers validate access tokens locally with the environment&apos;s public keys. The SDKs check the
        signature (RS256 only), issuer, audience and expiry for you, and expose the token&apos;s roles and scopes.
        Add your API&apos;s identifier as an <em>audience</em> on every application that calls it.
      </p>
      <h2>ASP.NET Core: <code>Dovepeak.Identity</code></h2>
      <pre>{`dotnet add package Dovepeak.Identity`}</pre>
      <pre>{`builder.Services.AddDovepeakAuthentication(builder.Configuration.GetSection("Dovepeak"));

app.MapGet("/orders", () => ...).RequireScope("orders:read");
app.MapDelete("/orders/{id}", (string id) => ...).RequireDovepeakRole("administrator");

// appsettings.json
{ "Dovepeak": { "Issuer": "<issuer>", "Audience": "orders-api" } }`}</pre>
      <p>Roles also work with <code>[Authorize(Roles = "administrator")]</code> and <code>User.IsInRole</code>. A complete example lives in <code>examples/dotnet-protected-api</code>.</p>
      <h2>Node.js: <code>@dovepeak/identity/node</code></h2>
      <pre>{`import { createTokenVerifier, errorResponse, requirePermissions } from "@dovepeak/identity/node";

const verifier = createTokenVerifier({ issuer: "<issuer>", audience: "orders-api" });

app.get("/orders", async (req, res) => {
  try {
    const token = await verifier.verifyAuthorizationHeader(req.headers.authorization);
    requirePermissions(token, { scopes: ["orders:read"] });
    res.json(await listOrders(token.subject));
  } catch (error) {
    const { status, headers } = errorResponse(error);   // 401 or 403
    res.status(status).set(headers).end();
  }
});`}</pre>
      <h2>Roles and scopes</h2>
      <ul>
        <li><code>roles</code>: the user&apos;s roles for the calling application (define and assign them on the application&apos;s Roles tab).</li>
        <li><code>scope</code>: space-separated scopes. Define them per environment, grant them to applications, and have applications request them.</li>
      </ul>
      <h2>Immediate revocation</h2>
      <p>
        Locally validated tokens stay valid until they expire (10 minutes by default). For operations that need
        immediate revocation, enable introspection: <code>Introspection:Enabled</code> in .NET, or the
        <code> introspection</code> option in Node.js, with your API&apos;s own confidential client credentials.
      </p>
    </article>
  );
}
