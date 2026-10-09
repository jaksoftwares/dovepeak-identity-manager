import { deleteApplication, updateApplication } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card } from "@/components/ui";
import { envPath, get, type ApplicationConfig, type Scope } from "@/lib/api";
import { can } from "@/lib/load";
import { applicationContext, type AppParams } from "./app";

export const metadata = { title: "Application settings" };

export default async function ApplicationSettings({ params }: { params: AppParams }) {
  const { org, application, ids, header } = await applicationContext(params, "settings");
  const [scopes, config] = await Promise.all([
    get<Scope[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/scopes")),
    get<ApplicationConfig>(envPath(ids.orgId, ids.projectId, ids.environmentId, `/applications/${application.id}/config`)),
  ]);
  const interactive = application.kind !== "machine";
  const write = can.write(org.role);
  const policy = application.tokenPolicy;

  return (
    <>
      {header}
      <Card title="Settings">
        <ActionForm action={updateApplication.bind(null, ids, application.id)} submitLabel="Save settings" resetOnSuccess={false}>
          <input type="hidden" name="kind" value={application.kind} />
          <fieldset disabled={!write} style={{ display: "grid", gap: 14, border: 0, padding: 0 }}>
            <label>Name<input name="name" defaultValue={application.name} required maxLength={200} /></label>
            {interactive && (
              <>
                <label>Callback URLs <span className="hint">One per line; exact match, no wildcards.</span>
                  <textarea name="redirectUris" defaultValue={application.redirectUris.join("\n")} />
                </label>
                <label>Logout URLs<textarea name="postLogoutRedirectUris" defaultValue={application.postLogoutRedirectUris.join("\n")} /></label>
                <label>Allowed origins (CORS)<textarea name="webOrigins" defaultValue={application.webOrigins.join("\n")} /></label>
              </>
            )}
            <label>API audiences<textarea name="audiences" defaultValue={application.audiences.join("\n")} /></label>
            <fieldset>
              <legend>Scopes this app may request</legend>
              {scopes.length === 0 ? <span className="muted small">No scopes are defined in this environment.</span> : (
                <div className="checks">
                  {scopes.map((s) => (
                    <label key={s.name}><input type="checkbox" name="scopes" value={s.name} defaultChecked={application.scopes.includes(s.name)} />{s.name}</label>
                  ))}
                </div>
              )}
            </fieldset>
            <fieldset>
              <legend>Token and session policy</legend>
              <p className="small muted">Leave empty to use the environment baseline (shown as the placeholder).</p>
              <div className="grid cols-3">
                <label>Access token lifetime (s) <span className="hint">300–3600</span>
                  <input name="accessTokenLifetimeSeconds" type="number" min={300} max={3600} defaultValue={policy.accessTokenLifetimeSeconds ?? ""} placeholder={String(config.accessTokenLifetimeSeconds)} />
                </label>
                {interactive && (
                  <>
                    <label>Session idle timeout (s) <span className="hint">300–1800</span>
                      <input name="sessionIdleTimeoutSeconds" type="number" min={300} max={1800} defaultValue={policy.sessionIdleTimeoutSeconds ?? ""} placeholder="1800" />
                    </label>
                    <label>Session maximum (s) <span className="hint">300–43200</span>
                      <input name="sessionMaxLifetimeSeconds" type="number" min={300} max={43200} defaultValue={policy.sessionMaxLifetimeSeconds ?? ""} placeholder="43200" />
                    </label>
                  </>
                )}
              </div>
            </fieldset>
          </fieldset>
        </ActionForm>
      </Card>

      <Card title="Authentication method">
        <p className="small">
          {interactive
            ? <>Authorization Code with PKCE (S256) is required. {application.kind === "web" ? "The client authenticates with its secret (client_secret_post)." : "Public client: no secret."} Implicit and password grants are rejected.</>
            : <>Client credentials grant with the client secret (client_secret_post). No end-user sessions.</>}
        </p>
      </Card>

      {write && (
        <Card title="Delete application">
          <p className="muted">The client is removed from the identity engine immediately; existing tokens expire on their own.</p>
          <ActionForm action={deleteApplication.bind(null, ids, application.id)} submitLabel="Delete application" submitClass="danger" confirm={`Delete ${application.name}?`} />
        </Card>
      )}
    </>
  );
}
