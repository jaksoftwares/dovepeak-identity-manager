import Link from "next/link";
import { PageGuide } from "@/components/help";
import { Card, Empty, PageHeader, When } from "@/components/ui";
import { orgPath, type AuditPage, type Organization } from "@/lib/api";
import { attempt, can, load } from "@/lib/load";

export const metadata = { title: "Audit log" };

const SOURCES = [
  { value: "", label: "All sources" },
  { value: "management", label: "Management (portal and API)" },
  { value: "authentication", label: "Authentication (end users)" },
  { value: "admin", label: "Identity engine administration" },
];

export default async function Audit({
  params,
  searchParams,
}: {
  params: Promise<{ orgId: string }>;
  searchParams: Promise<{ source?: string; type?: string; before?: string }>;
}) {
  const { orgId } = await params;
  const { source = "", type = "", before } = await searchParams;
  const org = await load<Organization>(orgPath(orgId));
  if (!can.readAudit(org.role)) {
    return (
      <>
        <PageHeader title="Audit log" />
        <Card><p className="muted">Admins and owners can read the audit log.</p></Card>
      </>
    );
  }

  const query = new URLSearchParams({ limit: "100" });
  if (source) query.set("source", source);
  if (before) query.set("before", before);
  const page = await attempt<AuditPage>(orgPath(orgId, `/audit-events?${query}`));
  const events = page.ok ? page.data.events.filter((e) => !type || e.type.toLowerCase().includes(type.toLowerCase())) : [];
  const next = page.ok && page.data.nextBefore
    ? `?${new URLSearchParams({ ...(source && { source }), ...(type && { type }), before: page.data.nextBefore })}`
    : undefined;

  return (
    <>
      <PageHeader title="Audit log" subtitle="Append-only. Management changes and end-user authentication events for this organization." />
      <PageGuide id="audit" title="Reading the audit log">
        <p>Every event is recorded here and kept for 365 days. Nobody can edit or delete entries. Three sources:</p>
        <ul>
          <li><strong>Management</strong>: changes made by your team in this portal or by API keys, such as <code>application.created</code>.</li>
          <li><strong>Authentication</strong>: what your end users do, such as <code>REGISTER</code>, <code>LOGIN</code>, <code>LOGIN_ERROR</code> (wrong password), <code>VERIFY_EMAIL</code> and <code>RESET_PASSWORD</code>. Appears within about 15 seconds.</li>
          <li><strong>Identity engine administration</strong>: low-level changes Dovepeak itself makes to the engine while applying your settings.</li>
        </ul>
        <p className="small">Investigating a user? Filter by <code>LOGIN_ERROR</code> to see failed attempts, then end their sessions under the environment&apos;s users page if needed.</p>
      </PageGuide>
      <Card>
        <form className="inline" method="get" style={{ marginBottom: 16 }}>
          <label>Source
            <select name="source" defaultValue={source}>
              {SOURCES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </label>
          <label>Event type contains<input name="type" defaultValue={type} placeholder="LOGIN_ERROR, application." /></label>
          <button type="submit" className="secondary">Filter</button>
        </form>
        {!page.ok ? <p className="muted">{page.message}</p> : events.length === 0 ? <Empty>No events match.</Empty> : (
          <table data-testid="audit-table">
            <thead><tr><th>When</th><th>Source</th><th>Event</th><th>Actor</th><th>Resource / client</th><th>IP</th></tr></thead>
            <tbody>
              {events.map((e) => (
                <tr key={e.id}>
                  <td className="small"><When value={e.occurredAt} /></td>
                  <td className="small">{e.source}</td>
                  <td><strong>{e.type}</strong>{e.error && <div className="small muted">{e.error}</div>}</td>
                  <td className="small">{e.actorType ? `${e.actorType} ${e.actorId ?? ""}` : "—"}</td>
                  <td className="small mono">{e.clientId ?? (e.resourceType ? `${e.resourceType} ${e.resourceId ?? ""}` : "—")}</td>
                  <td className="small">{e.ipAddress ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {next && <p style={{ marginTop: 12 }}><Link href={`/orgs/${orgId}/audit${next}`}>Older events →</Link></p>}
      </Card>
    </>
  );
}
