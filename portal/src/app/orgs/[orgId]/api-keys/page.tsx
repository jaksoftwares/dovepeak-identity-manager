import { createApiKey, revokeApiKey } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Badge, Card, Empty, PageHeader, When } from "@/components/ui";
import { orgPath, type ApiKey, type Organization } from "@/lib/api";
import { API_KEY_SCOPES, attempt, can, load } from "@/lib/load";

export const metadata = { title: "API keys" };

export default async function ApiKeys({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  if (!can.manageApiKeys(org.role)) {
    return (
      <>
        <PageHeader title="API keys" />
        <Card><p className="muted">Admins and owners manage API keys for automation.</p></Card>
      </>
    );
  }

  const keys = await attempt<ApiKey[]>(orgPath(orgId, "/api-keys"));

  return (
    <>
      <PageHeader title="API keys" subtitle="For CI pipelines and infrastructure-as-code. Keys can never exceed your own permissions." />
      <Card>
        {!keys.ok ? <p className="muted">{keys.message}</p> : keys.data.length === 0 ? <Empty>No API keys yet.</Empty> : (
          <table>
            <thead><tr><th>Name</th><th>Prefix</th><th>Scopes</th><th>Expires</th><th>Last used</th><th>Status</th><th /></tr></thead>
            <tbody>
              {keys.data.map((k) => (
                <tr key={k.id}>
                  <td>{k.name}</td>
                  <td className="mono">{k.prefix}…</td>
                  <td className="small">{k.scopes.join(", ")}</td>
                  <td><When value={k.expiresAt} /></td>
                  <td><When value={k.lastUsedAt} /></td>
                  <td><Badge value={k.revokedAt ? "revoked" : k.expiresAt && Date.parse(k.expiresAt) < Date.now() ? "expired" : "active"} /></td>
                  <td>
                    {!k.revokedAt && (
                      <ActionForm action={revokeApiKey.bind(null, orgId, k.id)} submitLabel="Revoke" submitClass="danger" className="inline" confirm={`Revoke ${k.name}? It stops working immediately.`} />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      <Card title="Create an API key">
        <ActionForm action={createApiKey.bind(null, orgId)} submitLabel="Create key">
          <label>Name<input name="name" required maxLength={100} placeholder="ci-deploy" /></label>
          <fieldset>
            <legend>Scopes</legend>
            <div className="checks">
              {API_KEY_SCOPES.map((s) => (
                <label key={s}><input type="checkbox" name="scopes" value={s} defaultChecked={s.endsWith(":read")} />{s}</label>
              ))}
            </div>
          </fieldset>
          <label>Expires on <span className="hint">Optional. Rotate by creating a new key, deploying it, then revoking the old one.</span>
            <input name="expiresAt" type="date" />
          </label>
        </ActionForm>
      </Card>
    </>
  );
}
