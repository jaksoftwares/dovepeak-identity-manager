import { createScope, deleteScope } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, Empty, When } from "@/components/ui";
import { envPath, get, type Scope } from "@/lib/api";
import { can } from "@/lib/load";
import { environmentContext, type EnvParams } from "../env";

export const metadata = { title: "API scopes" };

export default async function Scopes({ params }: { params: EnvParams }) {
  const { org, ids, header } = await environmentContext(params, "scopes");
  const scopes = await get<Scope[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/scopes"));
  const write = can.write(org.role);

  return (
    <>
      {header}
      <Card>
        <p className="muted small">
          Scopes describe what a token may do at your APIs. Grant them to applications; tokens carry granted scopes in the
          standard <code>scope</code> claim only when the application requests them.
        </p>
        {scopes.length === 0 ? <Empty>No scopes defined.</Empty> : (
          <table>
            <thead><tr><th>Scope</th><th>Description</th><th>Created</th>{write && <th />}</tr></thead>
            <tbody>
              {scopes.map((s) => (
                <tr key={s.name}>
                  <td className="mono">{s.name}</td>
                  <td>{s.description ?? ""}</td>
                  <td><When value={s.createdAt} /></td>
                  {write && <td><ActionForm action={deleteScope.bind(null, ids, s.name)} submitLabel="Delete" submitClass="danger" className="inline" confirm={`Delete scope ${s.name}?`} /></td>}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {write && (
        <Card title="Define a scope">
          <ActionForm action={createScope.bind(null, ids)} submitLabel="Create scope" className="inline">
            <label>Name<input name="name" required pattern="[a-z0-9][a-z0-9._:/\-]{0,63}" placeholder="orders:read" /></label>
            <label>Description<input name="description" maxLength={500} placeholder="Read orders" /></label>
          </ActionForm>
        </Card>
      )}
    </>
  );
}
