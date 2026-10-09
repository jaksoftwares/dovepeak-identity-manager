"use client";

import { useState } from "react";

export function CallApi() {
  const [result, setResult] = useState<string>();
  const [busy, setBusy] = useState(false);

  async function call() {
    setBusy(true);
    try {
      const response = await fetch("/api/protected", { cache: "no-store" });
      setResult(`${response.status}\n${JSON.stringify(await response.json(), null, 2)}`);
    } catch {
      setResult("Request failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="actions">
        <button className="button primary" type="button" onClick={call} disabled={busy}>
          {busy ? "Calling…" : "Call protected API"}
        </button>
      </div>
      {result && <pre>{result}</pre>}
    </>
  );
}
