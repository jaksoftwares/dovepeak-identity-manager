// Server-side configuration, read at runtime (never at build time): one image is configured per environment.
import "server-only";

function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing environment variable ${name}. See portal/.env.example.`);
  }
  return value;
}

export const config = {
  /** Public issuer of the platform realm; tokens are validated against it and browsers are redirected to it. */
  get issuer() {
    return new URL(required("PLATFORM_ISSUER"));
  },
  get clientId() {
    return process.env["PORTAL_CLIENT_ID"] ?? "dovepeak-portal";
  },
  /** Optional origin the server uses to reach the identity engine when the public URL is not reachable from it. */
  get identityInternalUrl() {
    return process.env["IDENTITY_INTERNAL_URL"] ? new URL(process.env["IDENTITY_INTERNAL_URL"]) : undefined;
  },
  get appUrl() {
    return new URL(required("APP_URL"));
  },
  get managementApiUrl() {
    return new URL(required("MANAGEMENT_API_URL"));
  },
  get sessionRedisUrl() {
    return required("SESSION_REDIS_URL");
  },
};

export const redirectUri = () => new URL("/api/auth/callback", config.appUrl).toString();

/** HTTPS deployments use the __Host- prefix: Secure, path "/", no Domain attribute. */
export const secureCookies = () => (process.env["APP_URL"] ?? "").startsWith("https:");
