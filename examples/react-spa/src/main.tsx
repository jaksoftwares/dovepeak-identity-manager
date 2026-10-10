import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { DovepeakError } from "@dovepeak/identity";
import { currentUser, getAccessToken, handleCallback, signIn, signOut } from "./auth";
import "./styles.css";

const API_URL = import.meta.env.VITE_API_URL;

function App() {
  const [ready, setReady] = useState(window.location.pathname !== "/callback");
  const [error, setError] = useState<string>();
  const [apiResult, setApiResult] = useState<string>();

  // Finish sign-in when the identity provider redirects back to /callback.
  useEffect(() => {
    if (window.location.pathname !== "/callback") return;
    handleCallback()
      .then((path) => window.history.replaceState(null, "", path))
      .catch((e: unknown) => setError(e instanceof DovepeakError ? `Sign-in failed (${e.code}).` : "Sign-in failed."))
      .finally(() => setReady(true));
  }, []);

  const user = currentUser();

  async function callApi() {
    const token = await getAccessToken();
    if (!token) {
      setApiResult("Your session ended. Please sign in again.");
      return;
    }

    const response = await fetch(new URL("/me", API_URL), { headers: { Authorization: `Bearer ${token}` } });
    setApiResult(`${response.status}\n${response.ok ? JSON.stringify(await response.json(), null, 2) : ""}`);
  }

  if (!ready) return <main className="card">Signing you in…</main>;

  return (
    <main className="card">
      <h1>React single-page app</h1>
      {error && <p className="notice" role="alert">{error}</p>}
      {user ? (
        <>
          <p data-testid="signed-in">Signed in as <strong>{user.email ?? user.sub}</strong></p>
          <div className="actions">
            <button type="button" onClick={callApi}>Call protected API</button>
            <button type="button" className="secondary" onClick={() => void signOut()}>Sign out</button>
          </div>
          {apiResult && <pre data-testid="api-result">{apiResult}</pre>}
        </>
      ) : (
        <>
          <p className="lead">Public client with Authorization Code + PKCE. Tokens stay in memory and are gone when you close the tab.</p>
          <div className="actions">
            <button type="button" onClick={() => void signIn()}>Sign in</button>
            <button type="button" className="secondary" onClick={() => void signIn({ register: true })}>Create account</button>
          </div>
        </>
      )}
    </main>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
