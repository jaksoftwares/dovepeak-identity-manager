// Server-side configuration. Importing this module from client code is a bug.
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
  issuer: new URL(required("PLATFORM_ISSUER")),
  clientId: process.env["PORTAL_CLIENT_ID"] ?? "dovepeak-portal",
  /** Optional origin the server uses to reach the identity engine when the public URL is not reachable from it. */
  identityInternalUrl: process.env["IDENTITY_INTERNAL_URL"] ? new URL(process.env["IDENTITY_INTERNAL_URL"]) : undefined,
  appUrl: new URL(required("APP_URL")),
  managementApiUrl: new URL(required("MANAGEMENT_API_URL")),
  sessionRedisUrl: required("SESSION_REDIS_URL"),
} as const;

export const redirectUri = new URL("/api/auth/callback", config.appUrl).toString();

/** HTTPS deployments use the __Host- prefix: Secure, path "/", no Domain attribute. */
export const secureCookies = config.appUrl.protocol === "https:";
