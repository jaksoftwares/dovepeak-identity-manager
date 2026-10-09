import { assignRole, createRole, deleteRole } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, Empty, When } from "@/components/ui";
import { envPath, get, type Role } from "@/lib/api";
import { can } from "@/lib/load";
import { applicationContext, type AppParams } from "../app";

export const metadata = { title: "Roles" };

export default async function Roles({ params }: { params: AppParams }) {
  const { org, application, ids, envBase, header } = await applicationContext(params, "roles");
  const roles = await get<Role[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, `/applications/${application.id}/roles`));
  const write = can.write(org.role);

  return (
    <>
      {header}
      <Card>
        <p className="muted small">Assigned roles appear in the user&apos;s access tokens for this application as a flat <code>roles</code> claim.</p>
        {roles.length === 0 ? <Empty>No roles defined.</Empty> : (
          <table>
            <thead><tr><th>Role</th><th>Description</th><th>Created</th>{write && <th />}</tr></thead>
            <tbody>
              {roles.map((r) => (
                <tr key={r.name}>
                  <td className="mono">{r.name}</td><td>{r.description ?? ""}</td><td><When value={r.createdAt} /></td>
                  {write && <td><ActionForm action={deleteRole.bind(null, ids, application.id, r.name)} submitLabel="Delete" submitClass="danger" className="inline" confirm={`Delete role ${r.name}?`} /></td>}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {write && (
        <div className="grid cols-2">
          <Card title="Create a role">
            <ActionForm action={createRole.bind(null, ids, application.id)} submitLabel="Create role">
              <label>Name <span className="hint">Keycloak administrative names such as admin are reserved; use e.g. administrator.</span>
                <input name="name" required maxLength={64} placeholder="editor" />
              </label>
              <label>Description<input name="description" maxLength={500} /></label>
            </ActionForm>
          </Card>
          <Card title="Assign or remove a role">
            <ActionForm action={assignRole.bind(null, ids, application.id)} submitLabel="Apply">
              <label>Role
                <select name="role" required>{roles.map((r) => <option key={r.name} value={r.name}>{r.name}</option>)}</select>
              </label>
              <label>User ID <span className="hint">Find it under <a href={`${envBase}/users`}>Users and sessions</a>.</span>
                <input name="userId" required className="mono" />
              </label>
              <label>Operation
                <select name="operation" defaultValue="assign"><option value="assign">Assign</option><option value="remove">Remove</option></select>
              </label>
            </ActionForm>
          </Card>
        </div>
      )}
    </>
  );
}
