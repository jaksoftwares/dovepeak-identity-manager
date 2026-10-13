import Link from "next/link";
import type { ReactNode } from "react";

export function PageHeader({
  title,
  subtitle,
  crumbs,
  actions,
}: {
  title: string;
  subtitle?: ReactNode;
  crumbs?: { href: string; label: string }[];
  actions?: ReactNode;
}) {
  return (
    <div className="page-header">
      <div>
        {crumbs && crumbs.length > 0 && (
          <div className="breadcrumbs">
            {crumbs.map((c, i) => (
              <span key={c.href}>
                {i > 0 && " / "}
                <Link href={c.href}>{c.label}</Link>
              </span>
            ))}
          </div>
        )}
        <h1>{title}</h1>
        {subtitle && <div className="subtitle">{subtitle}</div>}
      </div>
      {actions}
    </div>
  );
}

export function Card({ title, children, actions }: { title?: string; children: ReactNode; actions?: ReactNode }) {
  return (
    <section className="card">
      {(title || actions) && (
        <div className="row" style={{ justifyContent: "space-between", marginBottom: 8 }}>
          {title && <h2 style={{ margin: 0 }}>{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

export function Tabs({ items, current }: { items: { href: string; label: string }[]; current: string }) {
  return (
    <nav className="tabs" aria-label="Sections">
      {items.map((item) => (
        <Link key={item.href} href={item.href} aria-current={item.href === current ? "page" : undefined}>
          {item.label}
        </Link>
      ))}
    </nav>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>;
}

export interface Step {
  title: string;
  body: ReactNode;
  /** true: done; false: to do; undefined: the portal cannot tell (e.g. the role cannot read the audit log). */
  done?: boolean;
  href?: string;
  cta?: string;
}

/** A numbered checklist of next steps; the first unfinished step is highlighted. */
export function Checklist({ steps }: { steps: Step[] }) {
  const next = steps.findIndex((s) => s.done === false);
  return (
    <ol className="checklist">
      {steps.map((s, i) => (
        <li key={s.title} className={s.done ? "done" : i === next ? "next" : undefined}>
          <span className="marker" aria-hidden="true">{s.done ? "✓" : i + 1}</span>
          <div>
            <strong>{s.title}</strong>
            {s.done && <span className="sr-only"> (done)</span>}
            <div className="small muted">{s.body}</div>
            {!s.done && s.href && s.cta && <Link href={s.href} className="small">{s.cta} →</Link>}
          </div>
        </li>
      ))}
    </ol>
  );
}

/** Term-and-explanation pairs, for "what does each value mean" panels. */
export function Explain({ items }: { items: [term: ReactNode, meaning: ReactNode][] }) {
  return (
    <dl className="explain">
      {items.map(([term, meaning], i) => (
        <div key={i}>
          <dt>{term}</dt>
          <dd>{meaning}</dd>
        </div>
      ))}
    </dl>
  );
}

const stateClass: Record<string, string> = {
  ready: "ok", delivered: "ok", active: "ok",
  pending: "warn", deleting: "warn", retrying: "warn",
  failed: "danger", revoked: "danger", expired: "danger",
};

export function Badge({ value }: { value: string }) {
  return <span className={`badge ${stateClass[value.toLowerCase()] ?? ""}`}>{value}</span>;
}

export function When({ value }: { value?: string | null }) {
  if (!value) return <span className="muted">—</span>;
  const date = new Date(value);
  return <time dateTime={value} title={date.toISOString()}>{date.toLocaleString("en-GB", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" })} UTC</time>;
}
