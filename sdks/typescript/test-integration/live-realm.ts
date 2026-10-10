// Helpers for SDK contract tests against the running Keycloak (docker compose up). A throwaway realm with a
// public client, a confidential machine client and a verified user is created through the admin API with the
// least-privilege management account, and deleted afterwards.

const env = (name: string, fallback: string) => process.env[name] ?? fallback;

export const settings = {
  keycloakUrl: env("DOVEPEAK_IT_KEYCLOAK_URL", "http://localhost:8080"),
  adminUrl: env("DOVEPEAK_IT_KEYCLOAK_ADMIN_URL", "http://localhost:8081"),
  managementClientId: env("DOVEPEAK_IT_MANAGEMENT_CLIENT_ID", "dovepeak-management"),
  managementClientSecret: env("DOVEPEAK_IT_MANAGEMENT_CLIENT_SECRET", "management_client_local_only"),
};

export const REDIRECT_URI = "http://localhost:3999/callback";
export const API_AUDIENCE = "sdk-contract-api";

async function adminToken(): Promise<string> {
  const response = await fetch(`${settings.adminUrl}/realms/master/protocol/openid-connect/token`, {
    method: "POST",
    body: new URLSearchParams({
      grant_type: "client_credentials",
      client_id: settings.managementClientId,
      client_secret: settings.managementClientSecret,
    }),
  });
  if (!response.ok) throw new Error(`Admin token request failed: ${response.status}`);
  return ((await response.json()) as { access_token: string }).access_token;
}

async function admin(method: string, path: string, body?: unknown): Promise<Response> {
  const response = await fetch(`${settings.adminUrl}/admin/realms${path}`, {
    method,
    headers: { Authorization: `Bearer ${await adminToken()}`, "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok && response.status !== 404) {
    throw new Error(`${method} ${path} failed: ${response.status} ${await response.text()}`);
  }

  return response;
}

const audienceMapper = {
  name: "sdk-audience",
  protocol: "openid-connect",
  protocolMapper: "oidc-audience-mapper",
  config: { "included.custom.audience": API_AUDIENCE, "access.token.claim": "true", "id.token.claim": "false" },
};

export interface LiveRealm {
  name: string;
  issuer: string;
  publicClientId: string;
  machineClientId: string;
  machineSecret: string;
  user: { email: string; password: string };
  dispose(): Promise<void>;
}

export async function createLiveRealm(): Promise<LiveRealm> {
  const name = `it-sdk-${crypto.randomUUID().replaceAll("-", "").slice(0, 16)}`;
  const user = { email: `sdk-${crypto.randomUUID()}@example.test`, password: `Sdk-${crypto.randomUUID()}` };

  await admin("POST", "", {
    realm: name,
    enabled: true,
    sslRequired: "none",
    registrationEmailAsUsername: true,
    accessTokenLifespan: 300,
    revokeRefreshToken: true,
    refreshTokenMaxReuse: 0,
    clientScopes: undefined,
  });
  await admin("POST", `/${name}/clients`, {
    clientId: "sdk-spa",
    publicClient: true,
    standardFlowEnabled: true,
    directAccessGrantsEnabled: false,
    redirectUris: [REDIRECT_URI],
    webOrigins: ["http://localhost:3999"],
    attributes: { "pkce.code.challenge.method": "S256", "post.logout.redirect.uris": "http://localhost:3999/" },
    protocolMappers: [audienceMapper],
  });
  await admin("POST", `/${name}/clients`, {
    clientId: "sdk-machine",
    publicClient: false,
    serviceAccountsEnabled: true,
    standardFlowEnabled: false,
    secret: `machine-${crypto.randomUUID()}`,
    protocolMappers: [audienceMapper],
  });
  const machine = (await (await admin("GET", `/${name}/clients?clientId=sdk-machine`)).json()) as Array<{ id: string }>;
  const secret = (await (await admin("GET", `/${name}/clients/${machine[0]!.id}/client-secret`)).json()) as { value: string };

  await admin("POST", `/${name}/users`, {
    username: user.email,
    email: user.email,
    emailVerified: true,
    enabled: true,
    firstName: "Sdk",
    lastName: "Tester",
    credentials: [{ type: "password", value: user.password, temporary: false }],
  });

  return {
    name,
    issuer: `${settings.keycloakUrl}/realms/${name}`,
    publicClientId: "sdk-spa",
    machineClientId: "sdk-machine",
    machineSecret: secret.value,
    user,
    dispose: async () => {
      await admin("DELETE", `/${name}`);
    },
  };
}

/**
 * Plays the browser: opens the authorization URL, submits the hosted login form and returns the callback URL
 * (with code, state and iss) that Keycloak redirects to.
 */
export async function signInThroughHostedPage(authorizationUrl: URL, email: string, password: string): Promise<URL> {
  const cookies = new Map<string, string>();
  const send = async (url: string, init: RequestInit = {}) => {
    const response = await fetch(url, {
      ...init,
      redirect: "manual",
      headers: { ...(init.headers as Record<string, string>), Cookie: [...cookies].map(([k, v]) => `${k}=${v}`).join("; ") },
    });
    for (const header of response.headers.getSetCookie()) {
      const [pair] = header.split(";");
      const index = pair!.indexOf("=");
      cookies.set(pair!.slice(0, index), pair!.slice(index + 1));
    }

    return response;
  };

  const page = await send(authorizationUrl.toString());
  const html = await page.text();
  const action = /<form[^>]*id="kc-form-login"[^>]*action="([^"]+)"/.exec(html)?.[1]?.replaceAll("&amp;", "&");
  if (!action) throw new Error("Login form not found");

  const submitted = await send(action, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ username: email, password, credentialId: "" }).toString(),
  });
  const location = submitted.headers.get("location");
  if (submitted.status !== 302 || !location?.startsWith(REDIRECT_URI)) {
    throw new Error(`Sign-in did not redirect to the callback (HTTP ${submitted.status}).`);
  }

  return new URL(location);
}
