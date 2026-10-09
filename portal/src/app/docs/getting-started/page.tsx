export const metadata = { title: "Getting started" };

export default function GettingStarted() {
  return (
    <article>
      <h1>Getting started</h1>
      <p>This guide takes you from a new account to users signing in to your application.</p>

      <h2>1. Create an account and an organization</h2>
      <p>Choose <strong>Create an account</strong> on the portal home page, verify your email, then create an organization. You become its owner.</p>

      <h2>2. Create a project</h2>
      <p>Under <strong>Projects</strong>, create a project. Its three environments are provisioned within seconds; each has its own issuer URL, users and settings. Build against <em>development</em> and promote configuration to <em>production</em> when you are ready.</p>

      <h2>3. Register your application</h2>
      <p>Open the development environment and register an application. Pick the type that matches how your code runs:</p>
      <ul>
        <li><strong>Server-side web app or BFF</strong>: recommended for web apps. Tokens stay on your server; the browser holds a session cookie.</li>
        <li><strong>Single-page app</strong> or <strong>native app</strong>: public clients without a secret. PKCE is always required.</li>
        <li><strong>Machine-to-machine</strong>: backend services calling your APIs with the client credentials grant.</li>
      </ul>
      <p>Enter your exact callback URL (for example <code>http://localhost:3000/api/auth/callback</code> during development). Wildcards are not allowed. Confidential applications receive a client secret <strong>once</strong>: copy it into your secret store immediately.</p>

      <h2>4. Configure your application</h2>
      <p>The application&apos;s <strong>Integration</strong> tab shows the issuer, discovery URL and client ID, pre-filled. Any certified OpenID Connect library works; use Authorization Code with PKCE and request <code>openid email profile</code>.</p>

      <h2>5. Sign in</h2>
      <p>Start your app and sign in. Users register and recover passwords on the hosted pages, so passwords never touch your code. Find them under <strong>Users and sessions</strong>, and their sign-ins in the <strong>Audit log</strong>.</p>

      <h2>6. Protect your API</h2>
      <p>Add your API&apos;s identifier as an <em>audience</em> on the application, then validate tokens in the API: see <a href="/docs/protect-an-api">Protect an API</a>.</p>

      <h2>Token lifetimes</h2>
      <p>Access tokens last 10 minutes by default (5–60 configurable per application), sessions 30 minutes idle and 12 hours at most. Refresh tokens rotate on every use; reusing an old one ends the session.</p>
    </article>
  );
}
