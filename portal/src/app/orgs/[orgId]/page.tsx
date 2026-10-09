import Link from "next/link";
import { Card, Empty, PageHeader, When } from "@/components/ui";
import { get, orgPath, type AuditEvent, type AuditPage, type Member, type Organization, type Project } from "@/lib/api";
import { attempt, can, load } from "@/lib/load";

export const metadata = { title: "Overview" };

/** Authentication events that deserve a developer's attention. */
const SECURITY_EVENTS = new Set([
  "LOGIN_ERROR", "REFRESH_TOKEN_ERROR", "CLIENT_LOGIN_ERROR", "CODE_TO_TOKEN_ERROR", "RESET_PASSWORD", "UPDATE_PASSWORD",
  "UPDATE_CREDENTIAL", "REMOVE_CREDENTIAL", "UPDATE_EMAIL", "REVOKE_GRANT", "UPDATE_TOTP", "REMOVE_TOTP",
]);

export default async function Overview({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const [projects, members] = await Promise.all([
    get<Project[]>(orgPath(orgId, "/projects")),
    get<Member[]>(orgPath(orgId, "/members")),
  ]);
  const audit = can.readAudit(org.role)
    ? await attempt<AuditPage>(orgPath(orgId, "/audit-events?source=authentication&limit=200"))
    : undefined;

  const since = Date.now() - 7 * 24 * 3600 * 1000;
  const recent: AuditEvent[] = audit?.ok ? audit.data.events.filter((e) => Date.parse(e.occurredAt) >= since) : [];
  const count = (predicate: (e: AuditEvent) => boolean) => recent.filter(predicate).length;
  const security = recent.filter((e) => SECURITY_EVENTS.has(e.type)).slice(0, 10);

  return (
    <>
      <PageHeader title={org.name} subtitle={<>Slug <code>{org.slug}</code> · your role: {org.role}</>} />

      <div className="grid cols-4" style={{ marginBottom: 20 }}>
        <Stat name="Projects" value={projects.length} />
        <Stat name="Members" value={members.length} />
        <Stat name="Sign-ins (7 days)" value={audit?.ok ? count((e) => e.type === "LOGIN") : "—"} testId="stat-sign-ins" />
        <Stat name="Failed sign-ins (7 days)" value={audit?.ok ? count((e) => e.type === "LOGIN_ERROR") : "—"} testId="stat-failures" />
      </div>

      <div className="grid cols-2">
        <Card title="Authentication activity" actions={can.readAudit(org.role) ? <Link href={`/orgs/${orgId}/audit`}>Audit log</Link> : undefined}>
          {!audit ? (
            <p className="muted">Admins and owners see authentication activity for every environment.</p>
          ) : !audit.ok ? (
            <p className="muted">{audit.message}</p>
          ) : (
            <table>
              <tbody>
                <tr><td>Registrations</td><td>{count((e) => e.type === "REGISTER")}</td></tr>
                <tr><td>Token refreshes</td><td>{count((e) => e.type === "REFRESH_TOKEN")}</td></tr>
                <tr><td>Machine-to-machine tokens</td><td>{count((e) => e.type === "CLIENT_LOGIN")}</td></tr>
                <tr><td>Errors (all types)</td><td>{count((e) => e.type.endsWith("_ERROR"))}</td></tr>
              </tbody>
            </table>
          )}
        </Card>

        <Card title="Recent security events">
          {security.length === 0 ? (
            <Empty>No security events in the last 7 days.</Empty>
          ) : (
            <table>
              <thead><tr><th>Event</th><th>When</th><th>Error</th></tr></thead>
              <tbody>
                {security.map((e) => (
                  <tr key={e.id}><td>{e.type}</td><td><When value={e.occurredAt} /></td><td className="small">{e.error ?? ""}</td></tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>

      <Card title="Projects" actions={<Link href={`/orgs/${orgId}/projects`}>All projects</Link>}>
        {projects.length === 0 ? (
          <Empty>No projects yet. <Link href={`/orgs/${orgId}/projects`}>Create your first project</Link>.</Empty>
        ) : (
          <ul>
            {projects.slice(0, 8).map((p) => (
              <li key={p.id}><Link href={`/orgs/${orgId}/projects/${p.id}`}>{p.name}</Link> <span className="muted small">{p.slug}</span></li>
            ))}
          </ul>
        )}
      </Card>
    </>
  );
}

function Stat({ name, value, testId }: { name: string; value: number | string; testId?: string }) {
  return (
    <div className="stat" data-testid={testId}>
      <div className="value">{value}</div>
      <div className="name">{name}</div>
    </div>
  );
}
