import type { Metadata } from "next";
import Link from "next/link";
import { currentSession } from "@/lib/session";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: "Dovepeak Identity", template: "%s · Dovepeak Identity" },
  description: "Developer portal for Dovepeak Identity: organizations, projects, applications and credentials.",
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const current = await currentSession();
  const user = current?.session.user;

  return (
    <html lang="en">
      <body>
        <header className="topbar">
          <Link href={user ? "/orgs" : "/"} className="brand">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/dovepeak-mark.svg" alt="" />
            Dovepeak Identity
          </Link>
          <Link href="/docs">Docs</Link>
          <span className="spacer" />
          {user ? (
            <>
              <span className="user" data-testid="signed-in-as">{user.email ?? user.name ?? "Signed in"}</span>
              <form action="/api/auth/logout" method="post">
                <button type="submit" className="secondary">Sign out</button>
              </form>
            </>
          ) : (
            <a className="button" href="/api/auth/login">Sign in</a>
          )}
        </header>
        {children}
      </body>
    </html>
  );
}
