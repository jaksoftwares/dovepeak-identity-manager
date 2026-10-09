import { NavLink } from "@/components/nav";

export const metadata = { title: { default: "Documentation", template: "%s · Dovepeak Identity docs" } };

export default function DocsLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="shell">
      <nav className="sidebar" aria-label="Documentation">
        <div className="label">Guides</div>
        <NavLink href="/docs" exact>Overview</NavLink>
        <NavLink href="/docs/getting-started">Getting started</NavLink>
        <NavLink href="/docs/nextjs">Next.js (BFF)</NavLink>
        <NavLink href="/docs/protect-an-api">Protect an API</NavLink>
        <NavLink href="/docs/automation">Automation and webhooks</NavLink>
        <div className="label">Reference</div>
        <NavLink href="/docs/api">Management API</NavLink>
        <NavLink href="/docs/errors">Errors and troubleshooting</NavLink>
      </nav>
      <main className="main docs">{children}</main>
    </div>
  );
}
