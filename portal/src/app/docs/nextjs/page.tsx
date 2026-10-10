export const metadata = { title: "Next.js (BFF)" };

export default function NextGuide() {
  return (
    <article>
      <h1>Next.js with a backend-for-frontend</h1>
      <p>
        Keep tokens out of the browser. With <code>@dovepeak/identity/next</code> your Next.js server performs the OpenID
        Connect flow, stores tokens server-side (Redis or Valkey) and gives the browser only an opaque, HttpOnly session
        cookie. A complete, tested example lives in <code>examples/nextjs-bff</code>.
      </p>
      <h2>1. Register the application</h2>
      <p>Type <strong>Server-side web app or BFF</strong>, callback <code>https://your-app/api/auth/callback</code>, logout URL <code>https://your-app/</code>.</p>
      <h2>2. Install and configure</h2>
      <pre>{`npm install @dovepeak/identity ioredis`}</pre>
      <pre>{`// lib/auth.ts
import Redis from "ioredis";
import { cookies } from "next/headers";
import { createDovepeakAuth, redisSessionStore } from "@dovepeak/identity/next";

export const auth = createDovepeakAuth({
  client: {
    issuer: process.env.DOVEPEAK_ISSUER!,          // from the Integration tab
    clientId: process.env.DOVEPEAK_CLIENT_ID!,
    clientSecret: process.env.DOVEPEAK_CLIENT_SECRET, // server-side only; never NEXT_PUBLIC_
  },
  appUrl: process.env.APP_URL!,
  store: redisSessionStore(new Redis(process.env.SESSION_REDIS_URL!)),
});
export const session = () => auth.withCookies(cookies);`}</pre>
      <h2>3. Add the route handler</h2>
      <pre>{`// app/api/auth/[...dovepeak]/route.ts
import { auth } from "@/lib/auth";
export const { GET, POST } = auth;   // /api/auth/login, /callback, /logout (POST), /session`}</pre>
      <h2>4. Use the session</h2>
      <pre>{`// Server component or route handler
const user = await session().getSession();          // null when signed out
const token = await session().getAccessToken();     // refreshed automatically, one refresh at a time

// Client component
"use client";
import { useAuth, useSession } from "@dovepeak/identity/react";
const { status, user } = useSession();
const { signIn, signOut } = useAuth();`}</pre>
      <p>Wrap client components in <code>&lt;DovepeakProvider initialUser={"{user}"}&gt;</code>, passing the user you already loaded on the server.</p>
      <h2>Security checklist</h2>
      <ul>
        <li>Serve over HTTPS in production: the SDK then uses <code>Secure</code>, <code>__Host-</code> cookies and refuses plain HTTP.</li>
        <li>Send <code>Referrer-Policy: same-origin</code> (not <code>no-referrer</code>, which breaks the sign-out origin check), <code>frame-ancestors &apos;none&apos;</code> and <code>nosniff</code>.</li>
        <li>Never put tokens in <code>localStorage</code>, URLs or client components. See the browser threat model in <code>docs/sdk</code>.</li>
      </ul>
    </article>
  );
}
