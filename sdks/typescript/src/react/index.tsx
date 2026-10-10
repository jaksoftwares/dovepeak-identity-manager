"use client";

// @dovepeak/identity/react: session state for React apps backed by a Dovepeak backend-for-frontend.
// The browser never sees tokens: hooks read the user from the BFF's session endpoint, and sign-in/sign-out
// are full-page navigations handled by the server.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

export interface SessionUser {
  sub: string;
  email?: string;
  name?: string;
}

export type SessionState =
  | { status: "loading"; user: null }
  | { status: "authenticated"; user: SessionUser }
  | { status: "unauthenticated"; user: null };

interface ContextValue {
  session: SessionState;
  basePath: string;
  reload(): Promise<void>;
}

const SessionContext = createContext<ContextValue | null>(null);

export function DovepeakProvider({
  children,
  basePath = "/api/auth",
  initialUser,
}: {
  children: ReactNode;
  /** Mount path of the BFF route handler. */
  basePath?: string;
  /** User already known from server rendering (skips the first request). Use `null` for signed out. */
  initialUser?: SessionUser | null;
}) {
  const [session, setSession] = useState<SessionState>(
    initialUser === undefined
      ? { status: "loading", user: null }
      : initialUser
        ? { status: "authenticated", user: initialUser }
        : { status: "unauthenticated", user: null },
  );

  const reload = useCallback(async () => {
    try {
      const response = await fetch(`${basePath}/session`, { credentials: "same-origin", cache: "no-store" });
      const body = (await response.json()) as { authenticated: boolean; user?: SessionUser };
      setSession(body.authenticated && body.user ? { status: "authenticated", user: body.user } : { status: "unauthenticated", user: null });
    } catch {
      setSession({ status: "unauthenticated", user: null });
    }
  }, [basePath]);

  useEffect(() => {
    if (initialUser === undefined) void reload();
  }, [initialUser, reload]);

  const value = useMemo(() => ({ session, basePath, reload }), [session, basePath, reload]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

function useDovepeak(): ContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error("Wrap your app in <DovepeakProvider> to use Dovepeak hooks.");
  return context;
}

/** The current session: `loading`, `authenticated` (with `user`) or `unauthenticated`. */
export function useSession(): SessionState & { reload(): Promise<void> } {
  const { session, reload } = useDovepeak();
  return { ...session, reload };
}

/** Sign-in and sign-out actions. */
export function useAuth() {
  const { basePath } = useDovepeak();
  return {
    /** Navigates to the hosted sign-in page (or account creation with `register`). */
    signIn(options: { returnTo?: string; register?: boolean } = {}) {
      const params = new URLSearchParams();
      if (options.returnTo) params.set("returnTo", options.returnTo);
      if (options.register) params.set("register", "1");
      window.location.assign(`${basePath}/login${params.size > 0 ? `?${params}` : ""}`);
    },
    /** Signs out everywhere: a real form POST, so the server's Origin check protects it against CSRF. */
    signOut() {
      const form = document.createElement("form");
      form.method = "post";
      form.action = `${basePath}/logout`;
      document.body.appendChild(form);
      form.submit();
    },
  };
}

/** Renders `children` only for signed-in users; `fallback` otherwise (and while loading). */
export function SignedIn({ children, fallback = null }: { children: ReactNode; fallback?: ReactNode }) {
  const { status } = useSession();
  return <>{status === "authenticated" ? children : fallback}</>;
}

/** Renders `children` only for signed-out users. */
export function SignedOut({ children }: { children: ReactNode }) {
  const { status } = useSession();
  return <>{status === "unauthenticated" ? children : null}</>;
}
