import { createBrowserClient, TokenRequestError, type AuthorizationTransaction, type TokenSet, type UserClaims } from "@dovepeak/identity";

// A single-page app is a public client: it cannot keep a secret, so it uses Authorization Code + PKCE and keeps
// tokens in memory only. Nothing is written to localStorage (readable by any script on the page); the PKCE
// transaction lives in sessionStorage for the few seconds of the redirect and is deleted on use.
// For web apps with a server, prefer a backend-for-frontend (@dovepeak/identity/next).

export const client = createBrowserClient({
  issuer: import.meta.env.VITE_DOVEPEAK_ISSUER,
  clientId: import.meta.env.VITE_DOVEPEAK_CLIENT_ID,
  redirectUri: new URL("/callback", window.location.origin),
});

const TRANSACTION_KEY = "dovepeak:transaction";
let tokens: TokenSet | undefined;
let user: UserClaims | undefined;
let refreshing: Promise<string | undefined> | undefined;

export const currentUser = () => user;

export async function signIn(options: { register?: boolean } = {}) {
  const { url, transaction } = await client.createAuthorizationRequest({ register: options.register, returnTo: window.location.pathname });
  sessionStorage.setItem(TRANSACTION_KEY, JSON.stringify(transaction));
  window.location.assign(url);
}

/** Completes sign-in on /callback. Returns the path to continue to. */
export async function handleCallback(): Promise<string> {
  const stored = sessionStorage.getItem(TRANSACTION_KEY);
  sessionStorage.removeItem(TRANSACTION_KEY);
  if (!stored) throw new Error("No sign-in in progress.");

  const transaction = JSON.parse(stored) as AuthorizationTransaction;
  ({ tokens, user } = await client.completeAuthorization(window.location.href, transaction));
  return transaction.returnTo && transaction.returnTo !== "/callback" ? transaction.returnTo : "/";
}

/** A valid access token, refreshed shortly before expiry (one refresh at a time: refresh tokens are single-use). */
export async function getAccessToken(): Promise<string | undefined> {
  if (!tokens) return undefined;
  if (tokens.expiresAt - Date.now() > 30_000) return tokens.accessToken;
  if (!tokens.refreshToken) return undefined;

  refreshing ??= client
    .refresh(tokens.refreshToken)
    .then((next) => {
      tokens = { ...next, idToken: next.idToken ?? tokens?.idToken };
      return tokens.accessToken;
    })
    .catch((error: unknown) => {
      if (error instanceof TokenRequestError && error.requiresSignIn) {
        tokens = undefined;
        user = undefined;
        return undefined;
      }

      throw error;
    })
    .finally(() => {
      refreshing = undefined;
    });
  return refreshing;
}

export async function signOut() {
  const idToken = tokens?.idToken;
  tokens = undefined;
  user = undefined;
  const url = await client.endSessionUrl({ idToken, postLogoutRedirectUri: new URL("/", window.location.origin) });
  window.location.assign(url ?? "/");
}
