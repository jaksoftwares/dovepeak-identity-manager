"use client";

export default function Error({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <main className="hero">
      <h1>Something went wrong</h1>
      <p>The request could not be completed. If it keeps happening, quote this reference: {error.digest ?? "n/a"}.</p>
      <button type="button" onClick={() => reset()}>Try again</button>
    </main>
  );
}
