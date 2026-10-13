# Integration Guide

How to use Dovepeak Identity to add sign-in to your application: what it does for you, where your users' data lives, which credential goes where, and step-by-step integration for each kind of app. It also covers protecting your APIs, branding, emails, API keys and webhooks.

If you only want the platform running, see [Running the application](running-the-application.md) first.

---

## 1. What Dovepeak Identity does for you

Without an identity platform, every app team builds the same things: registration forms, password hashing, email verification, "forgot password" flows, lockout after failed attempts, sessions, token signing, role checks, and a place to see users. Each one is easy to get subtly wrong.

With Dovepeak Identity, your app does none of that. It **redirects users to Dovepeak's hosted sign-in pages** and gets back **signed tokens** that say who the user is and what they may do.

| You no longer build | Dovepeak provides |
| ------------------- | ----------------- |
| Sign-up, sign-in, sign-out pages | Hosted pages, with your logo and brand colour |
| Password storage | Argon2 hashing, a 12-character minimum, password history; your code never sees a password |
| Email verification, password reset | Emails sent for you, with your wording, through a relay that blocks email flooding |
| Brute-force protection | Lockout after failed attempts, plus rate limits at the edge |
| Sessions and tokens | OpenID Connect tokens, refresh with rotation and theft detection, configurable lifetimes |
| Roles and permissions | Roles and OAuth scopes in the token, checked in one line by the SDKs |
| User and session admin | A Users tab in the portal and API: find users, see and end their sessions |
| Audit trail | Every sign-in and admin change recorded per tenant, kept for 365 days |
| Separate dev/staging/prod setups | Three isolated environments per project, created automatically |

Because Dovepeak uses the open standards **OpenID Connect and OAuth 2.0**, any language or framework can integrate, not just the ones with a Dovepeak SDK.

---

## 2. Core concepts

```text
Organization            your company or team: members, API keys, webhooks, audit log
└── Project             one product, e.g. "Shop"
    ├── Branding        logo, colour, email wording (shared by all three environments)
    └── Environments    development · staging · production — each completely separate
        ├── Users       the people who sign in to your product (per environment)
        ├── Scopes      OAuth permissions such as orders:read
        └── Applications
            ├── Web app / BFF          (server-side web app; has a client secret)
            ├── Single-page app        (browser-only; no secret)
            ├── Native / mobile app    (no secret)
            └── Machine-to-machine     (backend service; has a client secret)
                └── Roles              e.g. administrator, editor — assigned to users
```

Things to know:

* **Each environment is fully isolated.** It has its own issuer URL, its own users, its own signing keys and its own applications. A user who registers in *development* does not exist in *production*. Build against development, then register the same applications in production when you go live.
* **An application is one piece of software that signs users in or calls your APIs.** Your website, your mobile app and your nightly job are three applications.
* **Developers and end users are different people.** *Developers* (you and your team) sign in to the **portal** and are organization members. *End users* (your customers) sign in to **your app** and live in a project environment. They never see the portal.

---

## 3. Where your users' data lives

This is the most common question, so here it is directly.

### What Dovepeak stores, and where

Your end users' **identity data** is stored **by Dovepeak, not in your database**. That covers email address, password hash, email-verified flag, sessions and role assignments.

* Each project environment is a separate *realm* in the identity engine (Keycloak).
* Realms are stored in the `keycloak` PostgreSQL database that the platform runs.
* If you self-host, that database is on your own server, and it is included in the platform's encrypted backups.
* You see and manage these users in the portal under **Environment → Users and sessions**, or through the Management API.

The portal is only a window onto this data. The data itself sits in the platform database behind it.

### What your app stores

Your app keeps **its own business data in its own database**: orders, profiles, preferences, subscriptions and so on. Link it to a Dovepeak user with the token's **`sub` claim** (the subject). `sub` is a stable, unique user ID that never changes, even if the user changes their email address.

```text
Dovepeak (identity)                         Your database (business data)
┌──────────────────────────────┐            ┌──────────────────────────────────────┐
│ sub: 6f1c…   email: ana@x.io │  ── sub ─▶ │ customers(id, dovepeak_sub, plan, …) │
│ password hash, sessions,     │            │ orders(id, customer_id, total, …)    │
│ roles, verified, lockout     │            └──────────────────────────────────────┘
└──────────────────────────────┘
```

**Never store passwords in your app.** You never receive them.

### Creating your own user record (the recommended pattern)

When a request arrives with a valid token, look up your record by `sub`. If there isn't one yet, create it. This is called just-in-time provisioning: your database fills itself as users sign in, with no syncing job needed.

```ts
// Node.js example, after verifying the token (see section 6)
const token = await verifier.verifyAuthorizationHeader(req.headers.authorization);
const customer =
  (await db.customers.findBySub(token.subject)) ??
  (await db.customers.insert({ dovepeakSub: token.subject, email: token.claims.email }));
```

Webhooks do **not** fire when an end user registers or signs in (see [section 9](#9-webhooks)). Don't wait for a "user created" event; use the pattern above.

---

## 4. Which credential goes where

The portal gives you several kinds of identifiers and secrets. They have different jobs and must never be mixed up.

| Credential | Where you get it | Where it goes | What it is for | Secret? |
| ---------- | ---------------- | ------------- | -------------- | ------- |
| **Issuer URL** | Application → Integration tab | Your app's configuration | The address of the environment's sign-in service, e.g. `http://localhost:8080/realms/dp-…` | No |
| **Client ID** | Application → Integration tab | Your app's configuration | Identifies your app to Dovepeak during sign-in | No |
| **Client secret** | Shown **once** when you create a web or machine application | Your app's **server-side** secret store or environment variables only | Proves your server is really your app when it exchanges codes for tokens | **Yes** |
| **Audience** (API identifier) | You choose it, e.g. `orders-api`, and add it to the application | Your **API's** configuration | Lets your API reject tokens meant for someone else | No |
| **API key** (`dpk_live_…`) | Organization → API keys, shown **once** | CI/CD pipelines, scripts, infrastructure code | Calls the **Management API** to configure Dovepeak itself (create projects, applications, roles) | **Yes** |
| **Webhook signing secret** | Shown **once** when you create a webhook | Your webhook receiver | Verifies that a webhook really came from Dovepeak | **Yes** |

**API keys are not for your app's sign-in.** Your app's users sign in with the issuer, client ID and (for web apps) client secret. API keys are for *automating Dovepeak's configuration*, for example a deploy script that registers a new application. Most apps never need one.

---

## 5. Integrating your app

### 5.1 Pick the application type

| Your app | Register it as | Library |
| -------- | -------------- | ------- |
| Next.js, or any web app with a server (Express, ASP.NET MVC, Django, Rails, Laravel, Spring…) | **Web application / BFF** (recommended) | Next.js: `@dovepeak/identity/next`. Others: any OpenID Connect library |
| React, Vue, Angular or Svelte app with **no** server of its own | **Single-page app** | `@dovepeak/identity` (`createBrowserClient`) |
| iOS, Android, React Native, Flutter, desktop | **Native / mobile app** | Any OIDC library with PKCE (AppAuth is the common choice) |
| Backend job or service calling your API with no user present | **Machine-to-machine** | `@dovepeak/identity` (`createServerClient().clientCredentials()`), or any OAuth client |
| Your API that receives tokens | *Not an application:* add its identifier as an **audience** | `@dovepeak/identity/node` or the `Dovepeak.Identity` NuGet package |

**Supported stacks.**

* Dovepeak has first-class SDKs for TypeScript/JavaScript (Next.js, React, browser, Node.js) and .NET (ASP.NET Core).
* Any other stack works through standard OpenID Connect: Python, Go, Java, PHP, Ruby, Rust and mobile platforms.
* For other stacks, use a well-maintained, certified OIDC library and configure it with:
  * the **issuer URL** (discovery happens automatically at `<issuer>/.well-known/openid-configuration`),
  * the **client ID**,
  * the **client secret** (web apps only),
  * **Authorization Code + PKCE**,
  * the scopes `openid email profile`.

**Only these flows are supported.** Authorization Code with PKCE handles user sign-in, and Client Credentials handles machine access. The legacy implicit and password grants are deliberately rejected; see [supported flows](supported-flows.md).

### 5.2 Register the application in the portal

1. Open your project and choose the **development** environment.
2. Under **Register an application**, pick the type from 5.1, fill in the fields and choose **Create application**.
3. Enter the **exact callback URL**, for example `http://localhost:3000/api/auth/callback`. Wildcards are not allowed; the URL must match character for character.
4. Enter the **logout URL** (where users land after signing out), for example `http://localhost:3000/`.
5. For a single-page app, add its origin under **allowed origins**, for example `http://localhost:5173`.
6. If the app calls your API, add the API's identifier (for example `orders-api`) as an **audience**.
7. For a web or machine app, **copy the client secret immediately.** It is never shown again; if you lose it, rotate it (section 5.7).
8. Open the **Integration** tab. It shows your issuer, discovery URL and client ID, ready to copy.

### 5.3 Next.js (recommended for web apps)

Your Next.js server runs the sign-in flow and keeps tokens in Redis or Valkey. The browser only gets an `HttpOnly` session cookie, so even an XSS bug cannot steal tokens.

```bash
npm install @dovepeak/identity ioredis
```

`.env.local`. These are all **server-side**; never use the `NEXT_PUBLIC_` prefix:

```dotenv
DOVEPEAK_ISSUER=http://localhost:8080/realms/dp-xxxxxxxx   # Integration tab
DOVEPEAK_CLIENT_ID=shop-web                                # Integration tab
DOVEPEAK_CLIENT_SECRET=...                                 # shown once at creation
APP_URL=http://localhost:3000
SESSION_REDIS_URL=redis://localhost:6379
```

```ts
// lib/auth.ts
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
```

```ts
// app/api/auth/[...dovepeak]/route.ts  →  /api/auth/login, /api/auth/callback, /api/auth/logout (POST), /api/auth/session
import { auth } from "@/lib/auth";
export const { GET, POST } = auth;
```

```tsx
// In a server component or route handler
const user = await session().getSession();        // null when signed out
const token = await session().getAccessToken();   // refreshed automatically; send it to your API
```

```tsx
// In a client component (wrap the tree in <DovepeakProvider initialUser={user}>)
"use client";
import { useAuth, useSession } from "@dovepeak/identity/react";

export function Account() {
  const { status, user } = useSession();
  const { signIn, signOut } = useAuth();
  return status === "authenticated"
    ? <button onClick={signOut}>Sign out {user.email}</button>
    : <button onClick={() => signIn()}>Sign in</button>;
}
```

To send users straight to **registration** instead of sign-in, link to `/api/auth/login?register=1`.

Serve the app with `Referrer-Policy: same-origin`; `no-referrer` breaks the sign-out CSRF check. A complete, tested app is in [examples/nextjs-bff](../examples/nextjs-bff/README.md).

### 5.4 Single-page app (React, Vue, Angular… with no server)

```ts
import { createBrowserClient } from "@dovepeak/identity";

const client = createBrowserClient({
  issuer: import.meta.env.VITE_DOVEPEAK_ISSUER,
  clientId: import.meta.env.VITE_DOVEPEAK_CLIENT_ID,     // public value; SPAs have no secret
  redirectUri: `${location.origin}/callback`,
});

// "Sign in" button
const { url, transaction } = await client.createAuthorizationRequest();
sessionStorage.setItem("tx", JSON.stringify(transaction));
location.assign(url);

// On /callback
const { tokens, user } = await client.completeAuthorization(location.href, JSON.parse(sessionStorage.getItem("tx")!));
sessionStorage.removeItem("tx");
// Keep tokens in memory (a variable or React state), never in localStorage.
```

The browser client refuses a client secret on purpose: anything shipped to a browser is public. For apps that have a server, prefer the BFF pattern in 5.3; see the [browser threat model](sdk/browser-threat-model.md). A complete example is in [examples/react-spa](../examples/react-spa/README.md).

### 5.5 Mobile and desktop apps

Register a **Native / mobile app** with a redirect URI using either:

* a custom scheme, for example `com.example.shop:/callback`, or
* a loopback address, for example `http://127.0.0.1:7890/callback`.

Then use an OpenID Connect library with PKCE, such as AppAuth for iOS or Android, configured with the issuer and client ID. Sign-in opens in the system browser, not an embedded web view, so users can trust the page and their password manager works.

### 5.6 Machine-to-machine (no user)

For a backend service that calls your API on its own behalf:

```ts
import { createServerClient } from "@dovepeak/identity";

const client = createServerClient({
  issuer: process.env.DOVEPEAK_ISSUER!,
  clientId: process.env.DOVEPEAK_CLIENT_ID!,
  clientSecret: process.env.DOVEPEAK_CLIENT_SECRET!,
  redirectUri: "http://localhost/unused",          // not used by the client credentials grant
});
const { accessToken } = await client.clientCredentials({ scopes: ["orders:read"] });
await fetch("https://api.example.com/orders", { headers: { Authorization: `Bearer ${accessToken}` } });
```

Grant the scope to the machine application and add the API as its audience in the portal.

### 5.7 Rotating a client secret without downtime

On the application's **Credentials** tab, choose **Issue a new secret** (or call `POST …/applications/{id}/secret`). The new secret is shown once, and **the old one keeps working for 24 hours**, so you can deploy the new one at your own pace.

If a secret has leaked, end the overlap immediately, using either of these:

* choose **Revoke previous secret** on the Credentials tab, or rotate with `?revokePrevious=true`, or
* call `DELETE …/secret/previous`.

---

## 6. Protecting your API

Your API never talks to Dovepeak on each request. It checks the token's signature locally against the environment's public keys (fetched once and cached). The SDKs check the signature (RS256 only), issuer, audience and expiry for you.

**Node.js**

```ts
import { createTokenVerifier, errorResponse, requirePermissions } from "@dovepeak/identity/node";

const verifier = createTokenVerifier({ issuer: process.env.DOVEPEAK_ISSUER!, audience: "orders-api" });

app.get("/orders", async (req, res) => {
  try {
    const token = await verifier.verifyAuthorizationHeader(req.headers.authorization);
    requirePermissions(token, { scopes: ["orders:read"] });    // or { roles: ["administrator"] }
    res.json(await listOrders(token.subject));                 // token.subject is the user's sub
  } catch (error) {
    const { status, headers } = errorResponse(error);          // 401 bad token, 403 missing permission
    res.status(status).set(headers).end();
  }
});
```

**ASP.NET Core**

```bash
dotnet add package Dovepeak.Identity
```

```csharp
builder.Services.AddDovepeakAuthentication(builder.Configuration.GetSection("Dovepeak"));

app.MapGet("/orders", () => ...).RequireScope("orders:read");
app.MapDelete("/orders/{id}", (string id) => ...).RequireDovepeakRole("administrator");
```

```json
{ "Dovepeak": { "Issuer": "http://localhost:8080/realms/dp-…", "Audience": "orders-api" } }
```

`[Authorize(Roles = "administrator")]` and `User.IsInRole(...)` also work. A complete example is in [examples/dotnet-protected-api](../examples/dotnet-protected-api).

**Other languages.** Use your stack's JWT library with these settings:

* fetch keys from `<issuer>/protocol/openid-connect/certs` (the `jwks_uri` in discovery),
* accept **RS256 only**,
* check `iss`, `aud` and `exp`,
* read permissions from `roles` and `scope`.

### Roles and scopes: which to use

| | Roles | Scopes |
| - | ----- | ------ |
| Answers | "What is this **user** allowed to do in this app?" | "What has this **application** been allowed to access?" |
| Example | `administrator`, `editor`, `support` | `orders:read`, `orders:write` |
| Defined | Per application, on its **Roles** tab | Per environment, then **granted** to applications |
| Assigned | To users, in the portal or `PUT …/roles/{role}/users/{userId}` | To applications; they appear in the token only when the app requests them |
| In the token | `roles` claim (a flat list) | `scope` claim (space-separated) |

### Revoking access immediately

A verified token stays valid until it expires: 10 minutes by default, configurable from 5 to 60 minutes per application. When you end a user's session in the portal, their next refresh fails.

For operations that must stop *instantly*, turn on **introspection** in the SDK. Use `Introspection:Enabled` in .NET, or the `introspection` option in Node.js, with your API's own client credentials. The API then asks Dovepeak about each token.

---

## 7. Branding and emails

Open your project and choose **Branding and emails**. These settings apply to all three environments.

| Setting | Effect |
| ------- | ------ |
| **Logo** | An HTTPS image URL, shown on the hosted sign-in, registration and password-reset pages and in emails |
| **Brand colour** | Buttons and accents. The text colour on buttons is chosen automatically so it stays readable |
| **Verification email** | Your subject line and introduction text |
| **Password-reset email** | Your subject line and introduction text |

### Emails Dovepeak sends for you

| Email | When |
| ----- | ---- |
| Verify your email | After registration; users must verify before their first sign-in |
| Reset your password | When a user asks; the link works once and expires after 15 minutes |
| Security alerts | When a user's password or sign-in methods change, or the account is locked |

Notes:

* **The links, the buttons and the "Secured by Dovepeak Identity" footer are always added by the platform.** You cannot remove them. This means a phishing email can't look exactly like a real one.
* Logo, colour and wording are the only branding controls today; layout and fonts are fixed ([known limitation L-07](security/known-limitations.md)).
* **Email limits** protect your users from inbox flooding: by default 5 emails per recipient per hour, 20 per day, and 500 per project environment per hour.
* **Locally**, every email is caught by Mailpit at http://localhost:8025, and nothing reaches real inboxes.
* **In production**, emails go through the SMTP provider the operator configures (`SMTP_*` in [self-hosting](self-hosting.md)).

---

## 8. API keys and the Management API

Everything you can do in the portal, you can also do through the **Management API** (`/v1`). This is useful for automation:

* registering applications from a deploy pipeline,
* setting up a new customer's project from your own back office,
* keeping environments consistent with code.

### Creating a key

1. Go to **Organization → API keys → Create an API key** (owners and admins only).
2. Choose the **scopes** the key needs. Pick only what the job requires:

   | Scope | Allows |
   | ----- | ------ |
   | `organization:read` | See the organization's details |
   | `members:read` | List members and their roles |
   | `projects:read` / `projects:write` | Read, or create and rename projects; edit branding and scopes |
   | `projects:delete` | Delete projects with all their environments and users |
   | `applications:read` / `applications:write` | Read, or register, change and delete applications and roles |
   | `credentials:manage` | Issue and revoke client secrets |
   | `users:manage` | List end users, assign roles, see and end sessions |
   | `audit:read` | Read the audit log |
   | `webhooks:manage` | Create and delete webhooks, read delivery history |

3. **Copy the key immediately.** It is shown once and stored only as a keyed digest.

A key can never have more power than the person who created it. It also can never manage members, API keys or the organization itself; those actions are reserved for people.

### Using it

```bash
curl -H "Authorization: Bearer $DOVEPEAK_API_KEY" \
  http://localhost:5080/v1/organizations/$ORG_ID/projects
```

```bash
# Create an application; the Idempotency-Key makes retries safe (no duplicates)
curl -X POST \
  -H "Authorization: Bearer $DOVEPEAK_API_KEY" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d @application.json \
  http://localhost:5080/v1/organizations/$ORG_ID/projects/$PROJECT_ID/environments/$ENV_ID/applications
```

When you create an organization or project, only `name` is required. Leave out `slug` and the API creates one from the name, adding `-2`, `-3`… if it is taken. For example, `{"name": "Online Shop"}` gets the slug `online-shop`.

From .NET:

```csharp
builder.Services.AddDovepeakManagementClient(builder.Configuration.GetSection("DovepeakManagement")); // BaseUrl, ApiKey

var project = await management.CreateProjectAsync(orgId, "shop", "Shop");
project = await management.WaitUntilReadyAsync(orgId, project.Id);   // environments are provisioned in the background
```

Main endpoints, all under `/v1/organizations/{orgId}`:

| Area | Endpoints |
| ---- | --------- |
| Projects | `GET/POST /projects`, `GET/PATCH/DELETE /projects/{id}`, `GET/PUT /projects/{id}/branding` |
| Environment | `GET /projects/{id}/environments/{envId}`, `GET/POST/DELETE …/scopes` |
| Applications | `GET/POST …/applications`, `GET/PATCH/DELETE …/applications/{appId}`, `GET …/config`, `POST …/secret`, `DELETE …/secret/previous` |
| Roles | `GET/POST/DELETE …/applications/{appId}/roles`, `PUT/DELETE …/roles/{role}/users/{userId}` |
| End users | `GET …/users?email=`, `GET/DELETE …/users/{userId}/sessions`, `DELETE …/sessions/{sessionId}` |
| Organization | `GET/POST /api-keys`, `GET/POST/DELETE /webhooks`, `GET /webhooks/{id}/deliveries`, `GET /audit-events?source=` |

The full, always-current reference is in the portal at **Docs → API reference**, and as OpenAPI at `/openapi/v1.json`. Errors use RFC 9457 Problem Details with a stable `code` field, for example `quota_exceeded`.

---

## 9. Webhooks

Webhooks tell **your systems** when something changes in your Dovepeak **configuration**. Typical uses:

* posting to Slack when someone rotates a secret,
* alerting security when drift is corrected,
* keeping an internal inventory of applications in sync.

### Setting one up

1. Go to **Organization → Webhooks → Add an endpoint**.
2. Enter an **HTTPS** URL. Private and internal addresses are refused.
3. Optionally limit it to specific event types.
4. **Copy the signing secret.** It is shown once.

### Events

| Area | Event types |
| ---- | ----------- |
| Organization and team | `organization.created`, `member.role_changed`, `member.removed`, `invitation.created`, `invitation.accepted`, `invitation.revoked` |
| Projects | `project.created`, `project.deleted`, `project.branding_updated`, `environment.ready` |
| Applications | `application.created`, `application.updated`, `application.deleted`, `application.secret_rotated`, `application.previous_secret_revoked` |
| Roles and scopes | `role.created`, `role.deleted`, `role.assigned`, `role.unassigned`, `scope.created`, `scope.deleted` |
| Sessions | `user.session_revoked`, `user.sessions_revoked` (a developer ended a user's sessions) |
| Security | `api_key.created`, `api_key.revoked`, `webhook.created`, `webhook.deleted`, `drift.corrected` (someone changed the identity engine directly and Dovepeak reverted it) |

**End-user activity is not sent as webhooks.** That includes registering, signing in, failing a password and verifying an email. Those events go to the **audit log**: read them in the portal under **Audit log**, or with `GET /v1/organizations/{orgId}/audit-events?source=authentication` (scope `audit:read`). Events arrive within about 15 seconds. To create your own user records, use the just-in-time pattern from [section 3](#3-where-your-users-data-lives).

### What a delivery looks like

```http
POST /dovepeak-webhook HTTP/1.1
Content-Type: application/json
Dovepeak-Event-Type: application.created
Dovepeak-Signature: t=1760000000,v1=5f2b…

{
  "id": "0b8e…",
  "type": "application.created",
  "occurredAt": "2026-10-13T09:30:00Z",
  "organizationId": "7c1d…",
  "resource": { "type": "application", "id": "3a9f…" },
  "actor": { "type": "user", "id": "…" },
  "data": { … }
}
```

### Verifying the signature (always do this)

The signature is an HMAC-SHA256 over `<t>.<raw body>`, using your webhook secret:

```ts
import { createHmac, timingSafeEqual } from "node:crypto";

export function verify(secret: string, header: string, rawBody: string): boolean {
  const parts = Object.fromEntries(header.split(",").map((p) => p.split("=")));
  if (Math.abs(Date.now() / 1000 - Number(parts.t)) > 300) return false;   // reject replays older than 5 minutes
  const expected = createHmac("sha256", secret).update(`${parts.t}.${rawBody}`).digest("hex");
  return expected.length === (parts.v1 ?? "").length && timingSafeEqual(Buffer.from(expected), Buffer.from(parts.v1));
}
```

Reply with a `2xx` status quickly and do slow work afterwards. Failed deliveries are retried with exponential backoff. Every attempt is listed in the webhook's **Delivery history**, and through `GET …/webhooks/{id}/deliveries`. Use the event `id` to ignore duplicates.

---

## 10. Managing your end users

Under **Environment → Users and sessions** in the portal (or with the API scope `users:manage`) you can:

* find users by email,
* see their active sessions and **end** one session or all of them (signs them out everywhere at their next token refresh),
* assign and remove **application roles**.

Users manage their own passwords through the hosted "forgot password" flow. They can also add an authenticator app (TOTP) in the account console, but this is optional and tenants cannot require it yet ([L-17](security/known-limitations.md)).

---

## 11. Going live checklist

1. **Register your applications in the production environment** with your real HTTPS URLs. Production has a different issuer, client IDs and secrets from development, and its own users.
2. **Store client secrets in a secret manager**, never in source code or browser bundles.
3. **Set token lifetimes** per application if the defaults (10-minute access tokens, 30-minute idle sessions, 12-hour maximum) don't fit.
4. **Add every API identifier as an audience**, and make sure your APIs check `aud`.
5. **Set branding** so users recognise your sign-in pages and emails.
6. **Turn on introspection** for APIs where a revoked session must stop working immediately.
7. **Subscribe a webhook** to `drift.corrected` and `application.secret_rotated` for security awareness.
8. **Plan secret rotation**: rotate, deploy within 24 hours, then let the old secret expire.

---

## 12. Troubleshooting

| Symptom | Cause and fix |
| ------- | ------------- |
| "Invalid parameter: redirect_uri" on the sign-in page | The callback URL doesn't **exactly** match the registered one. Check the scheme, port, path and trailing slash |
| Callback fails with a state or issuer error | The sign-in started in a different browser or tab, or the session store (Redis/Valkey) was cleared. Start sign-in again |
| API returns 401 with `reason: audience` | The token was not issued for this API. Add the API identifier as an audience on the calling application |
| API returns 401 with `reason: issuer` | The API is configured for a different environment, for example development tokens sent to a production API |
| API returns 403 | The token is valid, but the user lacks the role or the app lacks the scope. Assign the role, or grant and request the scope |
| Sign-out returns 403 in the Next.js SDK | The app sends `Referrer-Policy: no-referrer`; use `same-origin` |
| Users never get emails | Locally, check Mailpit (http://localhost:8025). Otherwise, the recipient may have hit the email quota (5 per hour); check `docker compose logs mail-relay` |
| A user is "locked" | Brute-force protection: they can try again after a short wait (1 to 15 minutes); it never locks permanently |
| A change made in the Keycloak admin console disappears | Expected: Dovepeak reverts changes made outside it within 5 minutes and sends `drift.corrected`. Make changes in the portal or API |

More: [supported flows and defaults](supported-flows.md) · [known limitations](security/known-limitations.md) · [browser threat model](sdk/browser-threat-model.md) · the portal's built-in docs at `/docs`.
