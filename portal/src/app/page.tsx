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
    </main>
  );
}
