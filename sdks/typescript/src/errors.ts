/**
 * Typed errors. Every error the SDK throws is a {@link DovepeakError} with a stable `code`, so applications can
 * branch on the cause without parsing messages. Messages never contain tokens or secrets.
 */
export class DovepeakError extends Error {
  readonly code: string;

  constructor(code: string, message: string, options?: { cause?: unknown }) {
    super(message, options);
    this.name = new.target.name;
    this.code = code;
  }
}

/** The SDK was configured unsafely or incompletely (for example a client secret in browser code). */
export class ConfigurationError extends DovepeakError {
  constructor(message: string) {
    super("configuration_error", message);
  }
}

/** The authorization server returned an error to the callback, or the callback failed validation (state, issuer). */
export class AuthorizationError extends DovepeakError {
  /** OAuth error code from the callback, such as `access_denied`, when the server sent one. */
  readonly error: string | undefined;

  constructor(message: string, error?: string, options?: { cause?: unknown }) {
    super("authorization_error", message, options);
    this.error = error;
  }
}

/** The token endpoint refused a request: `invalid_grant` (expired or reused refresh token), `invalid_scope`, … */
export class TokenRequestError extends DovepeakError {
  readonly error: string;

  constructor(error: string, description: string | undefined, options?: { cause?: unknown }) {
    super("token_request_failed", description ? `${error}: ${description}` : error, options);
    this.error = error;
  }

  /** True when the user must sign in again (the session ended, or the refresh token was used or revoked). */
  get requiresSignIn(): boolean {
    return this.error === "invalid_grant";
  }
}

export type TokenVerificationReason =
  | "malformed"
  | "signature"
  | "algorithm"
  | "expired"
  | "not_yet_valid"
  | "issuer"
  | "audience"
  | "claims"
  | "inactive";

/** An access token failed verification. Resource servers answer 401. */
export class TokenVerificationError extends DovepeakError {
  readonly reason: TokenVerificationReason;

  constructor(reason: TokenVerificationReason, message: string, options?: { cause?: unknown }) {
    super("invalid_token", message, options);
    this.reason = reason;
  }
}

/** A valid token lacks a required scope or role. Resource servers answer 403. */
export class ForbiddenError extends DovepeakError {
  readonly missing: { scopes: string[]; roles: string[] };

  constructor(missing: { scopes?: string[]; roles?: string[] }) {
    const scopes = missing.scopes ?? [];
    const roles = missing.roles ?? [];
    const parts = [
      scopes.length > 0 ? `scopes ${scopes.join(", ")}` : "",
      roles.length > 0 ? `roles ${roles.join(", ")}` : "",
    ].filter(Boolean);
    super("insufficient_permissions", `The token is missing required ${parts.join(" and ")}.`);
    this.missing = { scopes, roles };
  }
}

/** The identity provider could not be reached or answered unexpectedly. Usually transient. */
export class NetworkError extends DovepeakError {
  constructor(message: string, options?: { cause?: unknown }) {
    super("network_error", message, options);
  }
}
