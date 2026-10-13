import Link from "next/link";
import { PageGuide } from "@/components/help";
import { Card, Explain } from "@/components/ui";
import { envPath, get, type ApplicationConfig } from "@/lib/api";
import { applicationContext, type AppParams } from "../app";

export const metadata = { title: "Integration" };

/** Pre-filled integration guide (M4.6). Built from the public configuration endpoint, which never contains secrets. */
export default async function Integration({ params }: { params: AppParams }) {
  const { application, environment, ids, base, envBase, header } = await applicationContext(params, "integration");
  const config = await get<ApplicationConfig>(envPath(ids.orgId, ids.projectId, ids.environmentId, `/applications/${application.id}/config`));
  const redirect = config.redirectUris[0] ?? "https://app.example.com/auth/callback";
  const audience = config.audiences[0] ?? "your-api";
  const appUrl = originOf(redirect) ?? "http://localhost:3000";

  return (
    <>
      {header}
      <PageGuide id="integration" title="How to connect your app">
        <p>
          Your app sends users to Dovepeak&apos;s hosted sign-in page. After they sign in, your app receives signed tokens
          saying who the user is. These values tell your code where that sign-in page is and which application is asking:
        </p>
        <Explain
          items={[
            [<>Issuer</>, <>This <strong>{environment.kind}</strong> environment&apos;s sign-in service. Your app and your APIs are both configured with it. Not secret.</>],
            ["Discovery document", "Every endpoint your OpenID Connect library needs, found automatically. Most libraries only need the first value."],
            ["Client ID", "Identifies this application. Not secret: it can appear in browser code."],
            ["Client authentication", config.tokenEndpointAuthMethod === "none"
              ? "None: this app has no secret, because code shipped to browsers or phones cannot keep one."
              : <>Your server proves it is this app with the client secret, which was shown once when the application was
                  created. Lost it? Issue a new one on the <Link href={`${base}/credentials`}>Credentials</Link> tab.</>],
            ["PKCE", "A one-time code that stops an intercepted sign-in from being reused. SDKs and good libraries handle it for you."],
          ]}
        />
        <p className="small">
          The sign-in pages, password resets and verification emails are all handled by Dovepeak. The users who sign up appear
          under <Link href={`${envBase}/users`}>Users and sessions</Link>, and their user ID is the <code>sub</code> claim in
          their tokens: store it in your database to link your own data to each user.
        </p>
      </PageGuide>

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

      {config.kind === "machine" && (
        <Card title="Get a token (client credentials)">
          <p>Your service exchanges its client ID and secret for an access token, then sends it to your API in the <code>Authorization</code> header.</p>
          <h3>Node.js (@dovepeak/identity)</h3>
          <pre>{`import { createServerClient } from "@dovepeak/identity";

const client = createServerClient({
  issuer: "${config.issuer}",
  clientId: "${config.clientId}",
  clientSecret: process.env.DOVEPEAK_CLIENT_SECRET,
  redirectUri: "http://localhost/unused", // not used by this grant
});
const { accessToken } = await client.clientCredentials(${config.scopes.length > 0 ? `{ scopes: ${JSON.stringify(config.scopes)} }` : ""});
await fetch("https://api.example.com/orders", { headers: { Authorization: \`Bearer \${accessToken}\` } });`}</pre>
          <h3>Any language (HTTP)</h3>
          <pre>{`curl -X POST ${config.issuer}/protocol/openid-connect/token \\
  -d grant_type=client_credentials \\
  -d client_id=${config.clientId} \\
  -d client_secret=$DOVEPEAK_CLIENT_SECRET${config.scopes.length > 0 ? ` \\\n  -d "scope=${config.scopes.join(" ")}"` : ""}`}</pre>
          <p className="small muted">Keep the secret in your secret store and read it from the environment. <Link href={`${base}/credentials`}>Rotate it</Link> with a 24-hour overlap.</p>
        </Card>
      )}

      {config.kind === "web" && (
        <Card title="Server-side web app">
          <h3>Next.js: three files with the @dovepeak/identity SDK</h3>
          <p className="small">
            Your server runs the sign-in and keeps the tokens in Redis or Valkey. The browser only gets a session cookie, so
            tokens cannot be stolen by malicious scripts.
          </p>
          <pre>{`npm install @dovepeak/identity ioredis

# .env.local (server-side only: never prefix with NEXT_PUBLIC_)
DOVEPEAK_ISSUER=${config.issuer}
DOVEPEAK_CLIENT_ID=${config.clientId}
DOVEPEAK_CLIENT_SECRET=<shown once when the application was created>
APP_URL=${appUrl}
SESSION_REDIS_URL=redis://localhost:6379`}</pre>
          <pre>{`// lib/auth.ts
import Redis from "ioredis";
import { cookies } from "next/headers";
import { createDovepeakAuth, redisSessionStore } from "@dovepeak/identity/next";

export const auth = createDovepeakAuth({
  client: {
    issuer: process.env.DOVEPEAK_ISSUER!,
    clientId: process.env.DOVEPEAK_CLIENT_ID!,
    clientSecret: process.env.DOVEPEAK_CLIENT_SECRET,
  },
  appUrl: process.env.APP_URL!,
  store: redisSessionStore(new Redis(process.env.SESSION_REDIS_URL!)),
});
export const session = () => auth.withCookies(cookies);

// app/api/auth/[...dovepeak]/route.ts  (handles /api/auth/login, /callback, /logout, /session)
import { auth } from "@/lib/auth";
export const { GET, POST } = auth;

// anywhere on the server
const user = await session().getSession();       // null when signed out
const token = await session().getAccessToken();  // send to your API; refreshed automatically`}</pre>
          <p className="small">
            Link a &ldquo;Sign in&rdquo; button to <code>/api/auth/login</code> and &ldquo;Create account&rdquo; to{" "}
            <code>/api/auth/login?register=1</code>. Your callback URL must be <code>{appUrl}/api/auth/callback</code>.{" "}
            <Link href="/docs/nextjs">Full Next.js guide</Link>
          </p>
          <h3>Other frameworks (Express, ASP.NET, Django, Rails, Laravel, Spring…)</h3>
          <p className="small">
            Use your framework&apos;s OpenID Connect library with the issuer, client ID and secret above, the{" "}
            <em>Authorization Code</em> flow with PKCE, the scopes <code>openid email profile</code>, and the callback{" "}
            <code>{redirect}</code>.
          </p>
        </Card>
      )}

      {config.kind === "spa" && (
        <Card title="Single-page app">
          <pre>{`npm install @dovepeak/identity

import { createBrowserClient } from "@dovepeak/identity";

const client = createBrowserClient({
  issuer: "${config.issuer}",
  clientId: "${config.clientId}",
  redirectUri: "${redirect}",
});

// "Sign in" button
const { url, transaction } = await client.createAuthorizationRequest();
sessionStorage.setItem("tx", JSON.stringify(transaction));
location.assign(url);

// on the callback page
const { tokens, user } = await client.completeAuthorization(location.href, JSON.parse(sessionStorage.getItem("tx")!));
sessionStorage.removeItem("tx");
// keep tokens in memory (a variable or React state), never in localStorage`}</pre>
          <div className="alert warn">
            Code in the browser can be read by any script on the page. If your app has a server, register a server-side web
            app instead and keep tokens there.
          </div>
          <p className="small">Also add your app&apos;s address (for example <code>{appUrl}</code>) under Allowed origins in the settings.</p>
        </Card>
      )}

      {config.kind === "native" && (
        <Card title="Mobile or desktop app">
          <p>
            Use an OpenID Connect library with PKCE, such as AppAuth for iOS and Android, configured with the issuer and
            client ID above and the callback <code>{redirect}</code>. Sign-in opens in the system browser, so users see the
            real address bar and can use their password manager.
          </p>
          <pre>{`issuer:       ${config.issuer}
client_id:    ${config.clientId}
redirect_uri: ${redirect}
scopes:       ${["openid", "email", "profile", ...config.scopes].join(" ")}`}</pre>
        </Card>
      )}

      {config.kind !== "machine" && (
        <Card title="Any OpenID Connect library: the raw sign-in request">
          <p className="small">SDKs build this for you. It is shown here so you can see, and debug, what happens:</p>
          <pre>{`${config.issuer}/protocol/openid-connect/auth
  ?client_id=${config.clientId}
  &response_type=code
  &scope=${encodeURIComponent(["openid", "email", "profile", ...config.scopes].join(" "))}
  &redirect_uri=${encodeURIComponent(redirect)}
  &code_challenge=<S256 challenge>&code_challenge_method=S256
  &state=<random>&nonce=<random>`}</pre>
        </Card>
      )}

      <Card title="Protect your API">
        <p>
          Your API checks each request&apos;s access token itself, with no call to Dovepeak: the signature, that it was issued
          by this environment, that it is meant for this API (audience <code>{audience}</code>) and that it has not expired.
        </p>
        <pre>{`// ASP.NET Core (dotnet add package Dovepeak.Identity)
builder.Services.AddDovepeakAuthentication(o =>
{
    o.Issuer = "${config.issuer}";
    o.Audience = "${audience}";
});
app.MapGet("/orders", () => ...).RequireScope("orders:read");

// Node.js (npm install @dovepeak/identity)
import { createTokenVerifier, errorResponse, requirePermissions } from "@dovepeak/identity/node";
const verifier = createTokenVerifier({ issuer: "${config.issuer}", audience: "${audience}" });
const token = await verifier.verifyAuthorizationHeader(req.headers.authorization);
token.subject; // the user's ID: link your own data to it`}</pre>
        <p className="small">
          {config.audiences.length === 0 && <>This application has no API audience yet: add your API&apos;s identifier in the settings first. </>}
          <Link href="/docs/protect-an-api">Protecting an API</Link> covers roles (<code>roles</code> claim) and scopes (<code>scope</code> claim).
        </p>
      </Card>
    </>
  );
}

function originOf(url: string): string | undefined {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.origin : undefined;
  } catch {
    return undefined;
  }
}
