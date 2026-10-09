export const metadata = { title: "Next.js (BFF)" };

export default function NextGuide() {
  return (
    <article>
      <h1>Next.js with a backend-for-frontend</h1>
      <p>
        Keep tokens out of the browser. Your Next.js server performs the OpenID Connect flow, stores tokens server-side
        (for example in Redis or Valkey) and gives the browser only an opaque, HttpOnly session cookie. The repository
        contains a complete, tested example in <code>examples/nextjs-bff</code>.
      </p>
      <h2>Register the application</h2>
      <p>Type <strong>Server-side web app or BFF</strong>, callback <code>https://your-app/api/auth/callback</code>, logout URL <code>https://your-app/</code>.</p>
      <h2>Environment</h2>
      <pre>{`DOVEPEAK_ISSUER=<issuer from the Integration tab>
DOVEPEAK_CLIENT_ID=<client ID>
DOVEPEAK_CLIENT_SECRET=<client secret>   # server-side only; never NEXT_PUBLIC_
APP_URL=https://your-app
SESSION_REDIS_URL=redis://...`}</pre>
      <h2>The flow</h2>
      <ol>
        <li><code>GET /api/auth/login</code>: create PKCE verifier, state and nonce; store them server-side; redirect to the authorization endpoint.</li>
        <li><code>GET /api/auth/callback</code>: validate state and issuer, exchange the code with the verifier, validate the ID token and nonce, create the session.</li>
        <li>API routes read the session, refresh the access token shortly before it expires (one refresh at a time per session: refresh tokens are single-use), and call your APIs with it.</li>
        <li><code>POST /api/auth/logout</code>: check the Origin header, revoke the session server-side, then redirect to the end-session endpoint.</li>
      </ol>
      <h2>Checklist</h2>
      <ul>
        <li>Cookies: <code>HttpOnly</code>, <code>SameSite=Lax</code>, <code>Secure</code> and the <code>__Host-</code> prefix in production.</li>
        <li>No tokens in <code>localStorage</code>, URLs or client components.</li>
        <li>Security headers: <code>frame-ancestors &apos;none&apos;</code>, <code>nosniff</code>, strict referrer policy.</li>
      </ul>
    </article>
  );
}
