import Link from "next/link";
import { createWebhook, deleteWebhook } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
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
      <PageGuide id="webhooks" title="What webhooks tell you, and what they don't">
        <p>
          A webhook calls a URL on your server whenever something changes in this organization&apos;s <strong>configuration</strong>,
          for example to post in a team chat when a secret is rotated, or to alert security when someone tampers with settings.
        </p>
        <ul>
          <li>
            <strong>Events:</strong> organization, member and invitation changes; <code>project.created</code>,{" "}
            <code>project.deleted</code>, <code>project.branding_updated</code>, <code>environment.ready</code>;{" "}
            <code>application.created</code>, <code>.updated</code>, <code>.deleted</code>, <code>.secret_rotated</code>,{" "}
            <code>.previous_secret_revoked</code>; role, scope and session changes; <code>api_key.created</code>,{" "}
            <code>api_key.revoked</code>, <code>webhook.created</code>, <code>webhook.deleted</code>; and{" "}
            <code>drift.corrected</code> (someone changed the identity engine directly and Dovepeak reverted it).
          </li>
          <li>
            <strong>End users signing up or signing in do not trigger webhooks.</strong> That activity is in the audit log. To
            keep your own user records, create them the first time a user calls your app, using the user ID from their token.
          </li>
          <li>Each delivery is signed with a secret shown once when you add the endpoint. Always verify it (see below).</li>
        </ul>
      </PageGuide>
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
          <label>Event types <span className="hint">Optional. One per line, for example application.secret_rotated or drift.corrected. Leave empty to receive every event.</span>
            <textarea name="eventTypes" />
          </label>
        </ActionForm>
      </Card>
      <Card title="Verifying deliveries">
        <p className="small">Each request carries <code>Dovepeak-Signature: t=&lt;unix time&gt;,v1=&lt;hex&gt;</code>, an HMAC-SHA256 of <code>&lt;t&gt;.&lt;raw body&gt;</code> with your signing secret. Reject requests older than five minutes.</p>
        <pre>{`import { createHmac, timingSafeEqual } from "node:crypto";

export function verify(secret, header, rawBody) {
  const parts = Object.fromEntries(header.split(",").map((p) => p.split("=")));
  if (Math.abs(Date.now() / 1000 - Number(parts.t)) > 300) return false; // too old: possible replay
  const expected = createHmac("sha256", secret).update(\`\${parts.t}.\${rawBody}\`).digest("hex");
  return expected.length === (parts.v1 ?? "").length && timingSafeEqual(Buffer.from(expected), Buffer.from(parts.v1));
}`}</pre>
        <p className="small muted">Reply with a 2xx status quickly. Failed deliveries are retried with growing delays; click an endpoint to see every attempt.</p>
      </Card>
    </>
  );
}
