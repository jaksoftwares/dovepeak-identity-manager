// Server-side configuration. Importing this module from client code is a bug: it holds the client secret.
if (typeof window !== "undefined") {
  throw new Error("config.ts must only be imported on the server.");
}

function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`Missing environment variable ${name}. Run "dovepeak-dev demo-setup" or see .env.example.`);
  }
  return value;
}

export const config = {
  issuer: new URL(required("DOVEPEAK_ISSUER")),
  clientId: required("DOVEPEAK_CLIENT_ID"),
  clientSecret: required("DOVEPEAK_CLIENT_SECRET"),
  appUrl: new URL(required("APP_URL")),
  sessionRedisUrl: required("SESSION_REDIS_URL"),
  protectedApiUrl: new URL(required("PROTECTED_API_URL")),
} as const;

export const redirectUri = new URL("/api/auth/callback", config.appUrl).toString();

/** HTTPS deployments use the __Host- prefix: Secure, path "/", no Domain attribute. */
export const secureCookies = config.appUrl.protocol === "https:";
