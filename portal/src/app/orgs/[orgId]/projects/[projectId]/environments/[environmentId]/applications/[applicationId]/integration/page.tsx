import Link from "next/link";
import { Card } from "@/components/ui";
import { envPath, get, type ApplicationConfig } from "@/lib/api";
import { applicationContext, type AppParams } from "../app";

export const metadata = { title: "Integration" };

/** Pre-filled integration guide (M4.6). Built from the public configuration endpoint, which never contains secrets. */
export default async function Integration({ params }: { params: AppParams }) {
  const { application, ids, base, header } = await applicationContext(params, "integration");
  const config = await get<ApplicationConfig>(envPath(ids.orgId, ids.projectId, ids.environmentId, `/applications/${application.id}/config`));
  const redirect = config.redirectUris[0] ?? "https://app.example.com/auth/callback";
  const audience = config.audiences[0] ?? "your-api";

  return (
    <>
      {header}
      <Card title="Configuration">
        <table data-testid="integration-config">
          <tbody>
            <tr><td>Issuer</td><td className="mono small">{config.issuer}</td></tr>
            <tr><td>Discovery document</td><td className="mono small"><a href={config.discoveryUrl}>{config.discoveryUrl}</a></td></tr>
            <tr><td>Client ID</td><td className="mono small">{config.clientId}</td></tr>
            <tr><td>Client authentication</td><td className="mono small">{config.tokenEndpointAuthMethod}</td></tr>
            <tr><td>PKCE</td><td>{config.pkceRequired ? "Required (S256)" : "Not applicable"}</td></tr>
            <tr><td>Access token lifetime</td><td>{config.accessTokenLifetimeSeconds} s</td></tr>
            {config.scopes.length > 0 && <tr><td>Scopes</td><td className="mono small">{config.scopes.join(" ")}</td></tr>}
          </tbody>
        </table>
      </Card>

      {config.kind === "machine" ? (
        <Card title="Get a token (client credentials)">
          <pre>{`curl -X POST ${config.issuer}/protocol/openid-connect/token \\
  -d grant_type=client_credentials \\
  -d client_id=${config.clientId} \\
  -d client_secret=$DOVEPEAK_CLIENT_SECRET${config.scopes.length > 0 ? ` \\\n  -d "scope=${config.scopes.join(" ")}"` : ""}`}</pre>
          <p className="small muted">Keep the secret in your secret store and read it from the environment. <Link href={`${base}/credentials`}>Rotate it</Link> with a 24-hour overlap.</p>
        </Card>
      ) : (
        <Card title={config.kind === "web" ? "Server-side web app or BFF" : "Sign users in"}>
          <p>Use any certified OpenID Connect library with these settings. Environment variables (server-side):</p>
          <pre>{`DOVEPEAK_ISSUER=${config.issuer}
DOVEPEAK_CLIENT_ID=${config.clientId}${config.kind === "web" ? "\nDOVEPEAK_CLIENT_SECRET=<from the Credentials tab>" : ""}
DOVEPEAK_REDIRECT_URI=${redirect}`}</pre>
          <p>Start the Authorization Code flow with PKCE:</p>
          <pre>{`${config.issuer}/protocol/openid-connect/auth
  ?client_id=${config.clientId}
  &response_type=code
  &scope=${encodeURIComponent(["openid", "email", "profile", ...config.scopes].join(" "))}
  &redirect_uri=${encodeURIComponent(redirect)}
  &code_challenge=<S256 challenge>&code_challenge_method=S256
  &state=<random>&nonce=<random>`}</pre>
          {config.kind === "spa" && (
            <div className="alert warn">Browser apps should keep tokens out of JavaScript: prefer a backend-for-frontend (see the Next.js guide).</div>
          )}
          <p><Link href="/docs/nextjs">Next.js guide</Link> · <Link href="/docs/getting-started">Getting started</Link></p>
        </Card>
      )}

      <Card title="Protect your API">
        <p>Validate access tokens locally: signature from the JWKS, issuer, audience, expiry, and an explicit algorithm allow-list.</p>
        <pre>{`// ASP.NET Core
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = "${config.issuer}";
    o.TokenValidationParameters.ValidAudience = "${audience}";
    o.TokenValidationParameters.ValidAlgorithms = ["RS256"];
    o.MapInboundClaims = false;
});`}</pre>
        <p className="small"><Link href="/docs/protect-an-api">Protecting an API</Link> covers roles (<code>roles</code> claim) and scopes (<code>scope</code> claim).</p>
      </Card>
    </>
  );
}
