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
