import { DovepeakProvider } from "@dovepeak/identity/react";
import { session } from "@/lib/auth";
import { SignInButtons, SignOutButton } from "./auth-buttons";
import { CallApi } from "./call-api";

export const dynamic = "force-dynamic";

const errors: Record<string, string> = {
  expired: "The sign-in attempt expired. Please try again.",
  failed: "Sign-in could not be completed. Please try again.",
};

export default async function Home({ searchParams }: { searchParams: Promise<{ error?: string }> }) {
  const { error } = await searchParams;
  const user = await session().getSession();

  if (!user) {
    return (
      <section className="card">
        <h1>Welcome</h1>
        <p className="lead">
          This app delegates sign-in to Dovepeak Identity using Authorization Code with PKCE. Tokens stay on the
          server; your browser only holds an HttpOnly session cookie.
        </p>
        {error && errors[error] && <p className="notice">{errors[error]}</p>}
        <DovepeakProvider initialUser={null}>
          <SignInButtons />
        </DovepeakProvider>
      </section>
    );
  }

  return (
    <DovepeakProvider initialUser={user}>
      <section className="card">
        <h1>Signed in</h1>
        <dl>
          <dt>Email</dt>
          <dd>{user.email ?? "—"}</dd>
          <dt>Subject</dt>
          <dd>{user.sub}</dd>
        </dl>
        <SignOutButton />
      </section>
      <section className="card">
        <h1>Protected API</h1>
        <p className="lead">
          The BFF calls the .NET example API with your access token, refreshing it when needed. The API validates the
          token&apos;s signature, issuer, audience and expiry.
        </p>
        <CallApi />
      </section>
    </DovepeakProvider>
  );
}
