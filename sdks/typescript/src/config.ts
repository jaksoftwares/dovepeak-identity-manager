import { ConfigurationError } from "./errors.js";

/** Settings shared by every client. Copy them from the application's Integration tab in the portal. */
interface BaseClientConfig {
  /** The environment's issuer, e.g. https://id.example.com/realms/dp-… */
  issuer: string | URL;
  clientId: string;
  /** Exact callback URL registered for the application. */
  redirectUri: string | URL;
  /** Scopes to request. Defaults to `openid email profile`. */
  scopes?: string[];
  /**
   * Allow an `http://localhost` issuer. Only for local development stacks; never set in production.
   * Defaults to true only when the issuer host is localhost or 127.0.0.1.
   */
  allowInsecureLocalhost?: boolean;
  /** Custom fetch (for proxies or tests). Defaults to the global fetch. */
  fetch?: typeof fetch;
}

/**
 * Configuration safe to ship in browser bundles. It cannot carry a client secret: the type forbids it and
 * {@link createBrowserClient} rejects it at runtime, because anything in a bundle is public.
 */
export interface BrowserClientConfig extends BaseClientConfig {
  clientSecret?: never;
}

/** Configuration for server-side code (backend-for-frontend, services). May carry a client secret. */
export interface ServerClientConfig extends BaseClientConfig {
  clientSecret?: string;
}

export interface ResolvedConfig {
  issuer: URL;
  clientId: string;
  clientSecret: string | undefined;
  redirectUri: URL;
  scopes: string[];
  insecure: boolean;
  fetch: typeof fetch | undefined;
}

export const DEFAULT_SCOPES = ["openid", "email", "profile"];

export function isBrowser(): boolean {
  return typeof window !== "undefined" && typeof document !== "undefined";
}

export function resolveConfig(config: BaseClientConfig & { clientSecret?: string }): ResolvedConfig {
  if (!config || !config.issuer || !config.clientId || !config.redirectUri) {
    throw new ConfigurationError("issuer, clientId and redirectUri are required.");
  }

  const issuer = toUrl(config.issuer, "issuer");
  const redirectUri = toUrl(config.redirectUri, "redirectUri");
  const localIssuer = ["localhost", "127.0.0.1", "[::1]"].includes(issuer.hostname);
  const insecure = issuer.protocol === "http:";

  if (insecure && !(config.allowInsecureLocalhost ?? localIssuer)) {
    throw new ConfigurationError("The issuer must use HTTPS. Plain HTTP is only allowed for localhost during development.");
  }

  if (insecure && !localIssuer) {
    throw new ConfigurationError("Plain HTTP is only allowed for a localhost issuer.");
  }

  const scopes = config.scopes && config.scopes.length > 0 ? [...new Set(config.scopes)] : DEFAULT_SCOPES;
  return {
    issuer,
    clientId: config.clientId,
    clientSecret: config.clientSecret,
    redirectUri,
    scopes: scopes.includes("openid") ? scopes : ["openid", ...scopes],
    insecure,
    fetch: config.fetch,
  };
}

function toUrl(value: string | URL, name: string): URL {
  try {
    return new URL(value.toString());
  } catch {
    throw new ConfigurationError(`${name} must be an absolute URL.`);
  }
}
