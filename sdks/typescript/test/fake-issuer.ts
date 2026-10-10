import * as jose from "jose";

/**
 * An in-memory OpenID provider for unit tests: discovery, JWKS, token endpoint (authorization code, refresh,
 * client credentials), introspection and end-session. Injected through the SDK's `fetch` option.
 */
export async function createFakeIssuer(options: { issuer?: string; clientId?: string } = {}) {
  const issuer = options.issuer ?? "https://id.example.test/realms/dp-test";
  const clientId = options.clientId ?? "app_test";
  const { privateKey, publicKey } = await jose.generateKeyPair("RS256", { extractable: true });
  const jwk = { ...(await jose.exportJWK(publicKey)), kid: "key-1", alg: "RS256", use: "sig" };

  const state = {
    nonce: "",
    tokenResponses: [] as Array<() => Promise<Response>>,
    requests: [] as Array<{ url: string; body: URLSearchParams }>,
    refreshCount: 0,
    active: true,
  };

  const sign = (claims: jose.JWTPayload, ttlSeconds = 600) =>
    new jose.SignJWT(claims)
      .setProtectedHeader({ alg: "RS256", kid: "key-1", typ: "JWT" })
      .setIssuer(issuer)
      .setIssuedAt()
      .setExpirationTime(`${ttlSeconds}s`)
      .sign(privateKey);

  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

  const tokens = async (grant: string) => ({
    access_token: await sign({ sub: "user-1", azp: clientId, aud: ["orders-api"], scope: "openid email orders:read", roles: ["administrator"] }),
    token_type: "Bearer",
    expires_in: 600,
    ...(grant === "client_credentials"
      ? {}
      : {
          refresh_token: `refresh-${++state.refreshCount}`,
          refresh_expires_in: 1800,
          id_token: await sign({ sub: "user-1", aud: clientId, nonce: state.nonce, email: "user@example.test", email_verified: true }),
        }),
  });

  const fetch: typeof globalThis.fetch = async (input, init) => {
    const url = new URL(input instanceof Request ? input.url : input.toString());
    const body = new URLSearchParams(init?.body?.toString() ?? "");
    state.requests.push({ url: url.toString(), body });

    if (url.pathname.endsWith("/.well-known/openid-configuration")) {
      return json({
        issuer,
        authorization_endpoint: `${issuer}/protocol/openid-connect/auth`,
        token_endpoint: `${issuer}/protocol/openid-connect/token`,
        jwks_uri: `${issuer}/protocol/openid-connect/certs`,
        introspection_endpoint: `${issuer}/protocol/openid-connect/token/introspect`,
        end_session_endpoint: `${issuer}/protocol/openid-connect/logout`,
        id_token_signing_alg_values_supported: ["RS256"],
      });
    }

    if (url.pathname.endsWith("/certs")) return json({ keys: [jwk] });
    if (url.pathname.endsWith("/token/introspect")) return json({ active: state.active });
    if (url.pathname.endsWith("/logout")) return new Response(null, { status: 204 });
    if (url.pathname.endsWith("/token")) {
      const next = state.tokenResponses.shift();
      if (next) return next();
      return json(await tokens(body.get("grant_type") ?? ""));
    }

    return new Response("not found", { status: 404 });
  };

  return { issuer, clientId, fetch, state, sign, jwks: { keys: [jwk] }, privateKey, publicKey, json };
}
