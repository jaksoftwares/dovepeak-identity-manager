import Link from "next/link";
import { acceptInvitation, createOrganization } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
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
      <PageGuide id="organizations" title="Welcome — how Dovepeak Identity is organized">
        <p>
          Dovepeak Identity gives your applications sign-up, sign-in, password reset, email verification and roles, so you
          don&apos;t build them yourself. Your users sign in on Dovepeak&apos;s hosted pages; your app receives signed tokens
          saying who they are.
        </p>
        <ul>
          <li><strong>Organization</strong>: your company or team. It holds members, API keys, webhooks and the audit log.</li>
          <li><strong>Project</strong>: one product, such as &ldquo;Online shop&rdquo;. Each has three separate environments: development, staging and production.</li>
          <li><strong>Application</strong>: one piece of software that signs users in, such as your website, mobile app or a backend job.</li>
        </ul>
        <p>Start by creating an organization below. You become its owner and can invite colleagues later.</p>
      </PageGuide>

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
          <Empty>You are not a member of any organization yet. Create one below, or ask a colleague to invite you to theirs: invitations appear on this page.</Empty>
        ) : (
          <table>
            <thead><tr><th>Name</th><th>Identifier</th><th>Your role</th><th>Created</th></tr></thead>
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
          <details className="advanced">
            <summary>Advanced: choose the identifier yourself</summary>
            <label>
              Identifier (optional) <span className="hint">A short code such as acme, used by the API. Left empty, it is created for you. It cannot be changed later.</span>
              <input name="slug" pattern="[a-z][a-z0-9\-]{0,61}[a-z0-9]" placeholder="acme" />
            </label>
          </details>
        </ActionForm>
      </Card>
    </main>
  );
}
