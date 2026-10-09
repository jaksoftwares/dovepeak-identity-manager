import Link from "next/link";
import { createApplication } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, Empty } from "@/components/ui";
import { get, envPath, type Application, type Scope } from "@/lib/api";
import { can } from "@/lib/load";
import { environmentContext, type EnvParams } from "./env";

export const metadata = { title: "Applications" };

const KINDS = [
  { value: "spa", label: "Single-page app (public client, PKCE)" },
  { value: "web", label: "Server-side web app or BFF (confidential, PKCE)" },
  { value: "native", label: "Native or mobile app (public client, PKCE)" },
  { value: "machine", label: "Machine-to-machine service (client credentials)" },
];

export default async function Applications({ params }: { params: EnvParams }) {
  const { org, ids, base, header } = await environmentContext(params, "applications");
  const [applications, scopes] = await Promise.all([
    get<Application[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/applications")),
    get<Scope[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/scopes")),
  ]);

  return (
    <>
      {header}
      <Card>
        {applications.length === 0 ? <Empty>No applications in this environment yet.</Empty> : (
          <table>
            <thead><tr><th>Name</th><th>Type</th><th>Client ID</th><th>Scopes</th></tr></thead>
            <tbody>
              {applications.map((a) => (
                <tr key={a.id}>
                  <td><Link href={`${base}/applications/${a.id}`}>{a.name}</Link></td>
                  <td>{a.kind}</td>
                  <td className="mono small">{a.clientId}</td>
                  <td className="small">{a.scopes.join(", ") || "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {can.write(org.role) && (
        <Card title="Register an application">
          <ActionForm action={createApplication.bind(null, ids)} submitLabel="Create application">
            <label>Name<input name="name" required maxLength={200} placeholder="Storefront" /></label>
            <label>Type
              <select name="kind" defaultValue="spa">
                {KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
              </select>
            </label>
            <label>Callback URLs <span className="hint">One per line. Exact match, no wildcards; HTTPS except for localhost. Not used by machine apps.</span>
              <textarea name="redirectUris" placeholder="https://app.example.com/auth/callback" />
            </label>
            <label>Logout URLs <span className="hint">Where users may return after signing out.</span>
              <textarea name="postLogoutRedirectUris" placeholder="https://app.example.com/" />
            </label>
            <label>Allowed origins (CORS) <span className="hint">For browser apps calling the token endpoint, e.g. https://app.example.com</span>
              <textarea name="webOrigins" />
            </label>
            <label>API audiences <span className="hint">Resource servers this app calls; added to the token&apos;s aud claim.</span>
              <textarea name="audiences" placeholder="orders-api" />
            </label>
            {scopes.length > 0 && (
              <fieldset>
                <legend>Scopes this app may request</legend>
                <div className="checks">
                  {scopes.map((s) => <label key={s.name}><input type="checkbox" name="scopes" value={s.name} />{s.name}</label>)}
                </div>
              </fieldset>
            )}
          </ActionForm>
        </Card>
      )}
    </>
  );
}
