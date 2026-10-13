"use client";

import { useEffect, useRef, type ReactNode } from "react";

/**
 * "How this works" panel at the top of a page: what the page is for, the concepts on it and what to do next.
 * Open by default so newcomers see it; once collapsed it stays collapsed for that kind of page (per browser).
 */
export function PageGuide({ id, title = "How this works", children }: { id: string; title?: string; children: ReactNode }) {
  const ref = useRef<HTMLDetailsElement>(null);
  const key = `dp-guide:${id}`;

  useEffect(() => {
    try {
      if (ref.current && localStorage.getItem(key) === "closed") ref.current.open = false;
    } catch {
      // Storage can be unavailable (private mode, blocked site data); the panel then simply starts open.
    }
  }, [key]);

  return (
    <details
      ref={ref}
      open
      className="guide"
      onToggle={(event) => {
        try {
          localStorage.setItem(key, event.currentTarget.open ? "open" : "closed");
        } catch {
          // See above.
        }
      }}
    >
      <summary>{title}</summary>
      <div className="guide-body">{children}</div>
    </details>
  );
}
