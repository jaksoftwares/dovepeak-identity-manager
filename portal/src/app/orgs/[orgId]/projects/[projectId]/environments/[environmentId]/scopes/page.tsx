import { createScope, deleteScope } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
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
      <PageGuide id="scopes" title="API scopes: what an application may do at your APIs">
        <p>
          A <strong>scope</strong> is a named permission for one of your own APIs, such as <code>orders:read</code> or{" "}
          <code>orders:write</code>. You only need scopes if you build APIs that check them.
        </p>
        <ol>
          <li>Define the scope here, in this environment.</li>
          <li>Grant it to each application that needs it, in the application&apos;s settings.</li>
          <li>The application asks for it when it requests a token. The token then lists it in its <code>scope</code> claim.</li>
          <li>Your API checks it, for example with <code>RequireScope(&quot;orders:read&quot;)</code> in .NET or <code>requirePermissions(token, {"{ scopes: [\"orders:read\"] }"})</code> in Node.js.</li>
        </ol>
        <p className="small">
          Scopes limit what an <em>application</em> may do. To control what a <em>person</em> may do, such as
          administrators versus regular users, use the application&apos;s roles instead.
        </p>
      </PageGuide>
      <Card>
        {scopes.length === 0 ? <Empty>No scopes defined. Add one below when your API needs a permission.</Empty> : (
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
            <label>Name<input name="name" required pattern="[a-z0-9][a-z0-9._:/\-]{0,63}" placeholder="orders:read" title="Lowercase letters, digits and . _ : / -, for example orders:read" /></label>
            <label>Description<input name="description" maxLength={500} placeholder="Read orders" /></label>
          </ActionForm>
        </Card>
      )}
    </>
  );
}
