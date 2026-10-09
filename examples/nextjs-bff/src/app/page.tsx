import { currentSession } from "@/lib/session";
import { CallApi } from "./call-api";

export const dynamic = "force-dynamic";

const errors: Record<string, string> = {
  expired: "The sign-in attempt expired. Please try again.",
  failed: "Sign-in could not be completed. Please try again.",
};

export default async function Home({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const { error } = await searchParams;
  const current = await currentSession();

  if (!current) {
    return (
      <section className="card">
        <h1>Welcome</h1>
        <p className="lead">
          This app delegates sign-in to Dovepeak Identity using Authorization Code with PKCE. Tokens stay on the
          server; your browser only holds an HttpOnly session cookie.
        </p>
        {error && errors[error] && <p className="notice">{errors[error]}</p>}
        <div className="actions">
          <a className="button primary" href="/api/auth/login">
            Sign in
          </a>
          <a className="button" href="/api/auth/login?register=1">
            Create account
          </a>
        </div>
      </section>
    );
  }

  const { user } = current.session;
  return (
    <>
      <section className="card">
        <h1>Signed in</h1>
        <dl>
          <dt>Email</dt>
          <dd>{user.email ?? "—"}</dd>
          <dt>Subject</dt>
          <dd>{user.sub}</dd>
        </dl>
        <div className="actions">
          <form method="post" action="/api/auth/logout">
            <button className="button" type="submit">
              Sign out
            </button>
          </form>
        </div>
      </section>
      <section className="card">
        <h1>Protected API</h1>
        <p className="lead">
          The BFF calls the .NET example API with your access token, refreshing it when needed. The API validates the
          token&apos;s signature, issuer, audience and expiry.
        </p>
        <CallApi />
      </section>
    </>
  );
}
