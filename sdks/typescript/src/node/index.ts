// @dovepeak/identity/node: access token verification for resource servers (APIs).
import * as jose from "jose";
import { ConfigurationError, ForbiddenError, NetworkError, TokenVerificationError } from "../errors.js";

export { ForbiddenError, TokenVerificationError } from "../errors.js";
export type { TokenVerificationReason } from "../errors.js";

export interface TokenVerifierOptions {
  /** The environment's issuer. Tokens from any other issuer (another tenant, another environment) are rejected. */
  issuer: string;
  /** Your API's identifier: the token's `aud` must contain it. */
  audience: string | string[];
  /** Accepted signing algorithms. Defaults to RS256 only (prevents algorithm-confusion attacks). */
  algorithms?: string[];
  /** Allowed clock skew in seconds. Defaults to 30. */
  clockToleranceSeconds?: number;
  /**
   * Also ask the identity provider whether the token is still active (RFC 7662 introspection), so a revoked
   * session is rejected immediately instead of at expiry. Costs one request per verification.
   */
  introspection?: { clientId: string; clientSecret: string };
  /** Keys to use instead of fetching the issuer's JWKS (offline use and tests). */
  jwks?: jose.JSONWebKeySet;
  /** Custom fetch for discovery and introspection. */
  fetch?: typeof fetch;
}

/** A verified access token. */
export interface VerifiedToken {
  subject: string;
  /** The application the token was issued to (`azp`). */
  clientId: string | undefined;
  /** Granted OAuth scopes (`scope` claim). */
  scopes: string[];
  /** Application roles of the user (`roles` claim). */
  roles: string[];
  expiresAt: Date;
  claims: jose.JWTPayload;
}

export interface TokenVerifier {
  /** Verifies signature, issuer, audience, expiry and algorithm. Throws {@link TokenVerificationError}. */
  verify(token: string): Promise<VerifiedToken>;
  /** Verifies the `Authorization: Bearer …` header value. Throws {@link TokenVerificationError} when missing. */
  verifyAuthorizationHeader(header: string | null | undefined): Promise<VerifiedToken>;
}

export function createTokenVerifier(options: TokenVerifierOptions): TokenVerifier {
  if (!options?.issuer || !options.audience || (Array.isArray(options.audience) && options.audience.length === 0)) {
    throw new ConfigurationError("issuer and audience are required: never verify tokens without checking both.");
  }

  const issuer = options.issuer.replace(/\/+$/, "");
  const algorithms = options.algorithms ?? ["RS256"];
  if (algorithms.some((a) => a === "none" || a.startsWith("HS"))) {
    throw new ConfigurationError("Only asymmetric algorithms may be accepted.");
  }

  const doFetch = options.fetch ?? fetch;
  let metadata: Promise<{ jwks_uri: string; introspection_endpoint?: string }> | undefined;
  const discover = () => {
    metadata ??= doFetch(`${issuer}/.well-known/openid-configuration`)
      .then(async (response) => {
        if (!response.ok) throw new NetworkError(`Discovery failed with HTTP ${response.status}.`);
        return (await response.json()) as { jwks_uri: string; introspection_endpoint?: string };
      })
      .catch((error: unknown) => {
        metadata = undefined;
        throw error instanceof NetworkError ? error : new NetworkError("The identity provider could not be reached.", { cause: error });
      });
    return metadata;
  };

  let keys: jose.JWTVerifyGetKey | undefined = options.jwks ? jose.createLocalJWKSet(options.jwks) : undefined;
  const getKeys = async () => {
    if (!keys) {
      const { jwks_uri } = await discover();
      // Keys are cached and refetched when an unknown key ID appears (signing key rotation).
      keys = jose.createRemoteJWKSet(new URL(jwks_uri), { cooldownDuration: 30_000, cacheMaxAge: 600_000 });
    }

    return keys;
  };

  const verify = async (token: string): Promise<VerifiedToken> => {
    if (!token || token.split(".").length !== 3) {
      throw new TokenVerificationError("malformed", "The token is not a signed JWT.");
    }

    let payload: jose.JWTPayload;
    try {
      ({ payload } = await jose.jwtVerify(token, await getKeys(), {
        issuer,
        audience: options.audience,
        algorithms,
        clockTolerance: options.clockToleranceSeconds ?? 30,
        requiredClaims: ["exp", "iat"],
      }));
    } catch (error) {
      throw mapJoseError(error);
    }

    if (options.introspection) {
      await introspect(token);
    }

    const scope = typeof payload["scope"] === "string" ? payload["scope"] : "";
    const roles = Array.isArray(payload["roles"]) ? payload["roles"].filter((r): r is string => typeof r === "string") : [];
    return {
      subject: payload.sub ?? "",
      clientId: typeof payload["azp"] === "string" ? payload["azp"] : undefined,
      scopes: scope.split(" ").filter(Boolean),
      roles,
      expiresAt: new Date((payload.exp ?? 0) * 1000),
      claims: payload,
    };
  };

  const introspect = async (token: string) => {
    const { introspection_endpoint } = await discover();
    if (!introspection_endpoint) throw new ConfigurationError("The issuer does not publish an introspection endpoint.");
    const body = new URLSearchParams({
      token,
      client_id: options.introspection!.clientId,
      client_secret: options.introspection!.clientSecret,
    });
    let response: Response;
    try {
      response = await doFetch(introspection_endpoint, { method: "POST", body });
    } catch (error) {
      throw new NetworkError("The introspection endpoint could not be reached.", { cause: error });
    }

    if (!response.ok) throw new NetworkError(`Introspection failed with HTTP ${response.status}.`);
    const result = (await response.json()) as { active?: boolean };
    if (result.active !== true) {
      throw new TokenVerificationError("inactive", "The token has been revoked or its session has ended.");
    }
  };

  return {
    verify,
    async verifyAuthorizationHeader(header) {
      const match = /^Bearer\s+([A-Za-z0-9\-._~+/]+=*)$/i.exec(header?.trim() ?? "");
      if (!match) throw new TokenVerificationError("malformed", "Missing or malformed Authorization: Bearer header.");
      return verify(match[1]!);
    },
  };
}

/** True when the token carries every scope. */
export function hasScopes(token: VerifiedToken, ...scopes: string[]): boolean {
  return scopes.every((s) => token.scopes.includes(s));
}

/** True when the user has every role for the calling application. */
export function hasRoles(token: VerifiedToken, ...roles: string[]): boolean {
  return roles.every((r) => token.roles.includes(r));
}

/** Throws {@link ForbiddenError} (answer 403) unless the token has every scope and role. */
export function requirePermissions(token: VerifiedToken, required: { scopes?: string[]; roles?: string[] }): void {
  const scopes = (required.scopes ?? []).filter((s) => !token.scopes.includes(s));
  const roles = (required.roles ?? []).filter((r) => !token.roles.includes(r));
  if (scopes.length > 0 || roles.length > 0) throw new ForbiddenError({ scopes, roles });
}

/**
 * The HTTP status and `WWW-Authenticate` header for a verification failure (RFC 6750): 401 for invalid tokens,
 * 403 for missing permissions.
 */
export function errorResponse(error: unknown): { status: 401 | 403 | 503; headers: Record<string, string> } {
  if (error instanceof ForbiddenError) {
    return { status: 403, headers: { "WWW-Authenticate": `Bearer error="insufficient_scope"` } };
  }

  if (error instanceof TokenVerificationError) {
    return { status: 401, headers: { "WWW-Authenticate": `Bearer error="invalid_token"` } };
  }

  return { status: 503, headers: {} };
}

function mapJoseError(error: unknown): TokenVerificationError | NetworkError {
  if (error instanceof NetworkError) return error;
  if (error instanceof jose.errors.JWTExpired) return new TokenVerificationError("expired", "The token has expired.", { cause: error });
  if (error instanceof jose.errors.JWTClaimValidationFailed) {
    if (error.claim === "iss") return new TokenVerificationError("issuer", "The token was issued by another issuer.", { cause: error });
    if (error.claim === "aud") return new TokenVerificationError("audience", "The token was not issued for this API.", { cause: error });
    if (error.claim === "nbf") return new TokenVerificationError("not_yet_valid", "The token is not valid yet.", { cause: error });
    return new TokenVerificationError("claims", `The token's ${error.claim} claim is invalid.`, { cause: error });
  }
  if (error instanceof jose.errors.JOSEAlgNotAllowed) return new TokenVerificationError("algorithm", "The token's signing algorithm is not allowed.", { cause: error });
  if (error instanceof jose.errors.JWSSignatureVerificationFailed || error instanceof jose.errors.JWKSNoMatchingKey) {
    return new TokenVerificationError("signature", "The token's signature is invalid.", { cause: error });
  }
  if (error instanceof jose.errors.JWKSTimeout || error instanceof jose.errors.JWKSInvalid) {
    return new NetworkError("The issuer's signing keys could not be loaded.", { cause: error });
  }
  return new TokenVerificationError("malformed", "The token could not be parsed.", { cause: error });
}
