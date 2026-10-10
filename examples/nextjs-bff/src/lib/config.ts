// Server-side configuration, read at request time. Importing this module from client code is a bug: it holds the
// client secret.
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
  get issuer() { return required("DOVEPEAK_ISSUER"); },
  get clientId() { return required("DOVEPEAK_CLIENT_ID"); },
  get clientSecret() { return required("DOVEPEAK_CLIENT_SECRET"); },
  get appUrl() { return required("APP_URL"); },
  get sessionRedisUrl() { return required("SESSION_REDIS_URL"); },
  get protectedApiUrl() { return new URL(required("PROTECTED_API_URL")); },
};
