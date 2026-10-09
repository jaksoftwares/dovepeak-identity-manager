import Link from "next/link";
import { createWebhook, deleteWebhook } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Badge, Card, Empty, PageHeader, When } from "@/components/ui";
import { get, orgPath, type Organization, type Webhook } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Webhooks" };

export default async function Webhooks({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  if (!can.manageWebhooks(org.role)) {
    return (
      <>
        <PageHeader title="Webhooks" />
        <Card><p className="muted">Admins and owners configure webhooks.</p></Card>
      </>
    );
  }

  const webhooks = await get<Webhook[]>(orgPath(orgId, "/webhooks"));

  return (
    <>
      <PageHeader title="Webhooks" subtitle="Signed HTTPS notifications for management events, retried with exponential backoff." />
      <Card>
        {webhooks.length === 0 ? <Empty>No webhook endpoints yet.</Empty> : (
          <table>
            <thead><tr><th>URL</th><th>Events</th><th>Status</th><th>Created</th><th /></tr></thead>
            <tbody>
              {webhooks.map((w) => (
                <tr key={w.id}>
                  <td className="mono small"><Link href={`/orgs/${orgId}/webhooks/${w.id}`}>{w.url}</Link></td>
                  <td className="small">{w.eventTypes.length === 0 ? "All events" : w.eventTypes.join(", ")}</td>
                  <td><Badge value={w.active ? "active" : "disabled"} /></td>
                  <td><When value={w.createdAt} /></td>
                  <td><ActionForm action={deleteWebhook.bind(null, orgId, w.id)} submitLabel="Remove" submitClass="danger" className="inline" confirm="Remove this webhook endpoint?" /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      <Card title="Add an endpoint">
        <ActionForm action={createWebhook.bind(null, orgId)} submitLabel="Add endpoint">
          <label>Endpoint URL <span className="hint">HTTPS only. Private and internal network addresses are refused.</span>
            <input name="url" type="url" required placeholder="https://example.com/webhooks/dovepeak" />
          </label>
          <label>Event types <span className="hint">One per line, e.g. application.created, project.deleted, drift.corrected. Leave empty for all events.</span>
            <textarea name="eventTypes" />
          </label>
        </ActionForm>
      </Card>
      <Card title="Verifying deliveries">
        <p className="small">Each request carries <code>Dovepeak-Signature: t=&lt;unix time&gt;,v1=&lt;hex&gt;</code>, an HMAC-SHA256 of <code>&lt;t&gt;.&lt;raw body&gt;</code> with your signing secret. Reject requests older than five minutes.</p>
      </Card>
    </>
  );
}
