import { NavLink } from "@/components/nav";
import { orgPath, type Organization } from "@/lib/api";
import { load } from "@/lib/load";

export default async function OrganizationLayout({ children, params }: { children: React.ReactNode; params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const base = `/orgs/${orgId}`;

  return (
    <div className="shell">
      <nav className="sidebar" aria-label="Organization">
        <div className="label">{org.name}</div>
        <NavLink href={base} exact>Overview</NavLink>
        <NavLink href={`${base}/projects`}>Projects</NavLink>
        <NavLink href={`${base}/members`}>Members</NavLink>
        <NavLink href={`${base}/api-keys`}>API keys</NavLink>
        <NavLink href={`${base}/webhooks`}>Webhooks</NavLink>
        <NavLink href={`${base}/audit`}>Audit log</NavLink>
        <NavLink href={`${base}/settings`}>Settings</NavLink>
        <div className="label">Account</div>
        <NavLink href="/orgs" exact>All organizations</NavLink>
      </nav>
      <main className="main">{children}</main>
    </div>
  );
}
