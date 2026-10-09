import Link from "next/link";
import { acceptInvitation, createOrganization } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, Empty, PageHeader, When } from "@/components/ui";
import { get, type Invitation, type Organization } from "@/lib/api";

export const metadata = { title: "Organizations" };

export default async function Organizations() {
  const [organizations, invitations] = await Promise.all([
    get<Organization[]>("/v1/organizations"),
    get<Invitation[]>("/v1/me/invitations"),
  ]);

  return (
    <main className="main" style={{ margin: "0 auto" }}>
      <PageHeader title="Organizations" subtitle="Organizations own projects, members, API keys and webhooks." />

      {invitations.length > 0 && (
        <Card title="Invitations">
          <table>
            <thead><tr><th>Organization</th><th>Role</th><th>Expires</th><th /></tr></thead>
            <tbody>
              {invitations.map((i) => (
                <tr key={i.id}>
                  <td className="mono">{i.organizationId}</td>
                  <td>{i.role}</td>
                  <td><When value={i.expiresAt} /></td>
                  <td><ActionForm action={acceptInvitation.bind(null, i.id)} submitLabel="Accept" className="inline" /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      <Card title="Your organizations">
        {organizations.length === 0 ? (
          <Empty>You are not a member of any organization yet. Create one below.</Empty>
        ) : (
          <table>
            <thead><tr><th>Name</th><th>Slug</th><th>Your role</th><th>Created</th></tr></thead>
            <tbody>
              {organizations.map((o) => (
                <tr key={o.id}>
                  <td><Link href={`/orgs/${o.id}`}>{o.name}</Link></td>
                  <td className="mono">{o.slug}</td>
                  <td>{o.role}</td>
                  <td><When value={o.createdAt} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Create an organization">
        <ActionForm action={createOrganization} submitLabel="Create organization">
          <label>Name<input name="name" required maxLength={200} placeholder="Acme Inc." /></label>
          <label>
            Slug <span className="hint">2–63 lowercase letters, digits and hyphens. Used in URLs; cannot be changed.</span>
            <input name="slug" required pattern="[a-z][a-z0-9\-]{0,61}[a-z0-9]" placeholder="acme" />
          </label>
        </ActionForm>
      </Card>
    </main>
  );
}
