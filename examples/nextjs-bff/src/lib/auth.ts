import { cookies } from "next/headers";
import Redis from "ioredis";
import { createDovepeakAuth, redisSessionStore, type DovepeakAuth } from "@dovepeak/identity/next";
import { config } from "./config";

// Everything authentication-related comes from the Dovepeak SDK: Authorization Code + PKCE, server-side sessions
// in Valkey, single-flight token refresh and CSRF-protected sign-out. Created on first use, so building the app
// needs no configuration.
const globalForAuth = globalThis as unknown as { dovepeakAuth?: DovepeakAuth };

export function auth(): DovepeakAuth {
  globalForAuth.dovepeakAuth ??= createDovepeakAuth({
    client: {
      issuer: config.issuer,
      clientId: config.clientId,
      clientSecret: config.clientSecret,
    },
    appUrl: config.appUrl,
    store: redisSessionStore(new Redis(config.sessionRedisUrl), "bff:"),
  });
  return globalForAuth.dovepeakAuth;
}

/** Session helpers for server components and route handlers. */
export const session = () => auth().withCookies(cookies);
