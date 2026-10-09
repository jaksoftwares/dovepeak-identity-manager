import { Badge, Card, Empty, PageHeader, When } from "@/components/ui";
import { get, orgPath, type Webhook, type WebhookDelivery } from "@/lib/api";

export const metadata = { title: "Webhook deliveries" };

export default async function Deliveries({ params }: { params: Promise<{ orgId: string; webhookId: string }> }) {
  const { orgId, webhookId } = await params;
  const [webhooks, deliveries] = await Promise.all([
    get<Webhook[]>(orgPath(orgId, "/webhooks")),
    get<WebhookDelivery[]>(orgPath(orgId, `/webhooks/${webhookId}/deliveries`)),
  ]);
  const webhook = webhooks.find((w) => w.id === webhookId);

  return (
    <>
      <PageHeader
        title="Delivery history"
        subtitle={<code>{webhook?.url}</code>}
        crumbs={[{ href: `/orgs/${orgId}/webhooks`, label: "Webhooks" }]}
      />
      <Card>
        {deliveries.length === 0 ? <Empty>No deliveries yet.</Empty> : (
          <table>
            <thead><tr><th>Event</th><th>Status</th><th>Attempts</th><th>Last response</th><th>Created</th><th>Delivered</th></tr></thead>
            <tbody>
              {deliveries.map((d) => (
                <tr key={d.id}>
                  <td>{d.eventType}</td>
                  <td><Badge value={d.status} /></td>
                  <td>{d.attempts}</td>
                  <td>{d.lastStatusCode ?? "—"}</td>
                  <td><When value={d.createdAt} /></td>
                  <td><When value={d.deliveredAt} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
