import { redirect } from "next/navigation";
import { currentSession } from "@/lib/session";

const errors: Record<string, string> = {
  expired: "The sign-in attempt expired. Please try again.",
  failed: "Sign-in could not be completed. Please try again.",
};

export default async function Home({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  if (await currentSession()) redirect("/orgs");
  const { error } = await searchParams;

  return (
    <main className="hero">
      <h1>Authentication for every Dovepeak product</h1>
      <p>
        Create a project, register your applications and get standards-based sign-in — OpenID Connect, PKCE,
        short-lived tokens and audited administration — without building it yourself.
      </p>
      {error && errors[error] && <div className="alert error" role="alert">{errors[error]}</div>}
      <div className="row" style={{ justifyContent: "center", marginTop: 24 }}>
        <a className="button" href="/api/auth/login">Sign in</a>
        <a className="button secondary" href="/api/auth/login?register=1">Create an account</a>
        <a className="button secondary" href="/docs">Read the docs</a>
      </div>
      <ol className="grid cols-3 how" aria-label="How it works">
        <li className="card">
          <strong>1. Register your app</strong>
          <p className="small">Create a project and tell Dovepeak what your app is: a website, single-page app, mobile app or backend service.</p>
        </li>
        <li className="card">
          <strong>2. Copy a few settings</strong>
          <p className="small">Paste the issuer and client ID into your code, or use the SDKs for Next.js, React, Node.js and .NET.</p>
        </li>
        <li className="card">
          <strong>3. Your users sign in</strong>
          <p className="small">Sign-up, email verification, password reset and lockout are handled for you, on pages with your logo.</p>
        </li>
      </ol>
    </main>
  );
}
