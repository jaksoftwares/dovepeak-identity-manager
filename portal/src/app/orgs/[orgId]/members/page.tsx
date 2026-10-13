import { changeMemberRole, inviteMember, removeMember, revokeInvitation } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
import { Card, Empty, PageHeader, When } from "@/components/ui";
import { get, orgPath, type Invitation, type Member, type Organization } from "@/lib/api";
import { can, load } from "@/lib/load";
import { currentSession } from "@/lib/session";

export const metadata = { title: "Members" };

const ROLES = ["viewer", "developer", "admin", "owner"] as const;

function RoleSelect({ defaultValue }: { defaultValue?: string }) {
  return (
    <select name="role" defaultValue={defaultValue ?? "developer"} aria-label="Role">
      {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
    </select>
  );
}

export default async function Members({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const manage = can.manageMembers(org.role);
  const [members, invitations] = await Promise.all([
    get<Member[]>(orgPath(orgId, "/members")),
    manage ? get<Invitation[]>(orgPath(orgId, "/invitations")) : Promise.resolve([] as Invitation[]),
  ]);
  const me = (await currentSession())?.session.user.sub;

  return (
    <>
      <PageHeader title="Members" subtitle="Roles nest: viewer, developer, admin, owner. Only owners can grant the owner role." />
      <PageGuide id="members" title="Who can do what">
        <p>Members are your colleagues who use this portal. They are not the end users of your apps. Each role includes everything the role before it can do:</p>
        <table className="small">
          <thead><tr><th>Role</th><th>Can</th></tr></thead>
          <tbody>
            <tr><td><strong>viewer</strong></td><td>See projects, applications and members. Change nothing.</td></tr>
            <tr><td><strong>developer</strong></td><td>Create projects and applications, rotate client secrets, manage roles, scopes and end-user sessions.</td></tr>
            <tr><td><strong>admin</strong></td><td>Invite and remove members, delete projects, manage API keys and webhooks, read the audit log.</td></tr>
            <tr><td><strong>owner</strong></td><td>Rename or delete the organization and appoint other owners.</td></tr>
          </tbody>
        </table>
      </PageGuide>
      <Card>
        <table>
          <thead><tr><th>Email</th><th>Role</th><th>Joined</th>{manage && <th />}</tr></thead>
          <tbody>
            {members.map((m) => (
              <tr key={m.userId}>
                <td>{m.email}{m.userId === me && <span className="muted small"> (you)</span>}</td>
                <td>
                  {manage ? (
                    <ActionForm action={changeMemberRole.bind(null, orgId, m.userId)} submitLabel="Update" submitClass="secondary" className="inline" resetOnSuccess={false}>
                      <RoleSelect defaultValue={m.role} />
                    </ActionForm>
                  ) : m.role}
                </td>
                <td><When value={m.joinedAt} /></td>
                {manage && (
                  <td>
                    <ActionForm action={removeMember.bind(null, orgId, m.userId)} submitLabel="Remove" submitClass="danger" className="inline" confirm={`Remove ${m.email} from ${org.name}?`} />
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </Card>

      {manage && (
        <>
          <Card title="Pending invitations">
            {invitations.length === 0 ? <Empty>No pending invitations.</Empty> : (
              <table>
                <thead><tr><th>Email</th><th>Role</th><th>Expires</th><th /></tr></thead>
                <tbody>
                  {invitations.map((i) => (
                    <tr key={i.id}>
                      <td>{i.email}</td><td>{i.role}</td><td><When value={i.expiresAt} /></td>
                      <td><ActionForm action={revokeInvitation.bind(null, orgId, i.id)} submitLabel="Revoke" submitClass="danger" className="inline" /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Card>
          <Card title="Invite a member">
            <p className="muted small">
              Your colleague creates a portal account (or signs in) with this exact email address. The invitation then appears
              on their organizations page for them to accept. It expires after 7 days.
            </p>
            <ActionForm action={inviteMember.bind(null, orgId)} submitLabel="Send invitation" className="inline">
              <label>Email<input name="email" type="email" required placeholder="colleague@example.com" /></label>
              <label>Role<RoleSelect /></label>
            </ActionForm>
          </Card>
        </>
      )}
    </>
  );
}
