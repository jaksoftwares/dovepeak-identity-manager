import Link from "next/link";
import { PageGuide } from "@/components/help";
import { Card, Checklist, Empty, PageHeader, When, type Step } from "@/components/ui";
import {
  envPath, get, orgPath,
  type Application, type AuditEvent, type AuditPage, type Branding, type Member, type Organization, type Project,
} from "@/lib/api";
import { attempt, can, load, type Attempt } from "@/lib/load";

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
  const steps = await gettingStarted(orgId, projects, members, audit);

  return (
    <>
      <PageHeader title={org.name} subtitle={<>Identifier <code>{org.slug}</code> · your role: {org.role}</>} />

      {steps.some((s) => s.done !== true) && (
        <Card title="Get started">
          <p className="small muted" style={{ marginTop: -4 }}>
            From an empty organization to users signing in to your app. Steps tick themselves off as you complete them.
          </p>
          <Checklist steps={steps} />
        </Card>
      )}

      <div className="grid cols-4" style={{ marginBottom: 20 }}>
        <Stat name="Projects" value={projects.length} />
        <Stat name="Members" value={members.length} />
        <Stat name="Sign-ins (7 days)" value={audit?.ok ? count((e) => e.type === "LOGIN") : "—"} testId="stat-sign-ins" />
        <Stat name="Failed sign-ins (7 days)" value={audit?.ok ? count((e) => e.type === "LOGIN_ERROR") : "—"} testId="stat-failures" />
      </div>

      <PageGuide id="overview-activity" title="What these numbers mean">
        <p>
          The figures count your end users&apos; activity across every project and environment in this organization over the
          last 7 days. A <strong>sign-in</strong> is a user entering their password on the hosted page; a{" "}
          <strong>token refresh</strong> is an app quietly extending a session; <strong>machine-to-machine tokens</strong> are
          issued to backend services. Security events, such as failed sign-ins and password changes, are listed so you notice
          unusual activity. Full details are in the audit log.
        </p>
      </PageGuide>

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
              <li key={p.id}><Link href={`/orgs/${orgId}/projects/${p.id}`}>{p.name}</Link></li>
            ))}
          </ul>
        )}
      </Card>
    </>
  );
}

/** The onboarding checklist, worked out from what already exists in the organization. */
async function gettingStarted(orgId: string, projects: Project[], members: Member[], audit?: Attempt<AuditPage>): Promise<Step[]> {
  const project = projects[0];
  const development = project?.environments.find((e) => e.kind === "development" && e.state === "ready");
  const projectBase = project ? `/orgs/${orgId}/projects/${project.id}` : `/orgs/${orgId}/projects`;
  const envBase = development ? `${projectBase}/environments/${development.id}` : projectBase;

  const [applications, branding] = await Promise.all([
    development ? attempt<Application[]>(envPath(orgId, project!.id, development.id, "/applications")) : undefined,
    project ? attempt<Branding>(orgPath(orgId, `/projects/${project.id}/branding`)) : undefined,
  ]);
  const application = applications?.ok ? applications.data[0] : undefined;
  const signedIn = audit?.ok ? audit.data.events.some((e) => e.type === "LOGIN" || e.type === "REGISTER") : undefined;

  return [
    {
      title: "Create a project",
      body: "A project is one product. It comes with separate development, staging and production environments.",
      done: projects.length > 0,
      href: `/orgs/${orgId}/projects`,
      cta: "Create a project",
    },
    {
      title: "Register your application",
      body: "In the development environment, tell Dovepeak about your app: its type and the URL users return to after signing in.",
      done: application !== undefined,
      href: envBase,
      cta: "Register an application",
    },
    {
      title: "Connect your app and sign in a first user",
      body: "Copy the issuer and client ID from the application's Integration tab into your app, then register a test user through it.",
      done: signedIn,
      href: application ? `${envBase}/applications/${application.id}/integration` : envBase,
      cta: "Open the integration guide",
    },
    {
      title: "Add your logo and colours",
      body: "Make the sign-in pages and emails look like your product.",
      done: branding?.ok ? Boolean(branding.data.logoUrl || branding.data.primaryColor) : false,
      href: `${projectBase}/branding`,
      cta: "Customize branding",
    },
    {
      title: "Invite your team",
      body: "Give colleagues access with the right role: viewers read, developers build, admins manage.",
      done: members.length > 1,
      href: `/orgs/${orgId}/members`,
      cta: "Invite a member",
    },
  ];
}

function Stat({ name, value, testId }: { name: string; value: number | string; testId?: string }) {
  return (
    <div className="stat" data-testid={testId}>
      <div className="value">{value}</div>
      <div className="name">{name}</div>
    </div>
  );
}
