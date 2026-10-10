"use client";

import { useAuth } from "@dovepeak/identity/react";

/** Sign-in and sign-out through the SDK's React adapter: full-page navigations to the BFF, no tokens in the browser. */
export function SignInButtons() {
  const { signIn } = useAuth();
  return (
    <div className="actions">
      <button className="button primary" type="button" onClick={() => signIn()}>Sign in</button>
      <button className="button" type="button" onClick={() => signIn({ register: true })}>Create account</button>
    </div>
  );
}

export function SignOutButton() {
  const { signOut } = useAuth();
  return (
    <div className="actions">
      <button className="button" type="button" onClick={() => signOut()}>Sign out</button>
    </div>
  );
}
