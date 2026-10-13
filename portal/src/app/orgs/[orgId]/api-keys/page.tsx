import { createApiKey, revokeApiKey } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
import { Badge, Card, Empty, PageHeader, When } from "@/components/ui";
import { orgPath, type ApiKey, type Organization } from "@/lib/api";
import { API_KEY_SCOPES, attempt, can, load } from "@/lib/load";

export const metadata = { title: "API keys" };

const SCOPE_HELP: Record<string, string> = {
  "organization:read": "See the organization's details",
  "members:read": "List members and their roles",
  "projects:read": "List projects, environments, branding and scopes",
  "projects:write": "Create and rename projects; edit branding and scopes",
  "projects:delete": "Delete projects with all their environments and users",
  "applications:read": "List applications and their settings",
  "applications:write": "Register, change and delete applications and roles",
  "credentials:manage": "Issue and revoke client secrets",
  "users:manage": "List end users, assign roles, end sessions",
  "audit:read": "Read the audit log",
  "webhooks:manage": "Add and remove webhook endpoints",
};

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
      <PageGuide id="api-keys" title="API keys are for automation, not for signing users in">
        <p>
          An API key lets a script or pipeline do what you do in this portal, through the Management API: create projects,
          register applications, assign roles. <strong>Your apps do not need one to sign users in.</strong> Sign-in uses the
          issuer, client ID and client secret shown on each application.
        </p>
        <ul>
          <li>Typical uses: a deploy pipeline that registers its application, or your back office setting up a project per customer.</li>
          <li>Send it as <code>Authorization: Bearer dpk_live_…</code> to the Management API. The full reference is in Docs → API.</li>
          <li>Tick only the scopes the job needs. A key cannot manage members, other API keys or the organization itself.</li>
          <li>The key is shown once. Store it in your CI system&apos;s secret store; revoke it here if it leaks.</li>
        </ul>
      </PageGuide>
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
                <label key={s} title={SCOPE_HELP[s]}><input type="checkbox" name="scopes" value={s} defaultChecked={s.endsWith(":read")} />{s}</label>
              ))}
            </div>
            <p className="small muted" style={{ margin: "8px 0 0" }}>
              <code>:read</code> scopes only look; <code>:write</code> and <code>:manage</code> scopes change things. Hover a scope for details.
            </p>
          </fieldset>
          <label>Expires on <span className="hint">Optional. Rotate by creating a new key, deploying it, then revoking the old one.</span>
            <input name="expiresAt" type="date" />
          </label>
        </ActionForm>
      </Card>
    </>
  );
}
