import Link from "next/link";
import { createApplication } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
import { Card, Empty } from "@/components/ui";
import { get, envPath, type Application, type Scope } from "@/lib/api";
import { can } from "@/lib/load";
import { environmentContext, type EnvParams } from "./env";

export const metadata = { title: "Applications" };

const KINDS = [
  {
    value: "web",
    label: "Server-side web app",
    description: "Next.js, Express, ASP.NET, Django, Rails, Laravel… Your server signs users in and keeps their tokens. Recommended for websites. Gets a client secret.",
  },
  {
    value: "spa",
    label: "Single-page app",
    description: "React, Vue or Angular running only in the browser, with no server of its own. No secret, because anything in a browser is public.",
  },
  {
    value: "native",
    label: "Mobile or desktop app",
    description: "iOS, Android, React Native, Flutter or desktop. Sign-in opens in the system browser. No secret.",
  },
  {
    value: "machine",
    label: "Machine-to-machine service",
    description: "A backend job or service calling your API on its own, with no user signing in. Gets a client secret.",
  },
];

const KIND_LABEL = Object.fromEntries(KINDS.map((k) => [k.value, k.label]));

export default async function Applications({ params }: { params: EnvParams }) {
  const { org, environment, ids, base, header } = await environmentContext(params, "applications");
  const [applications, scopes] = await Promise.all([
    get<Application[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/applications")),
    get<Scope[]>(envPath(ids.orgId, ids.projectId, ids.environmentId, "/scopes")),
  ]);

  return (
    <>
      {header}
      <PageGuide id="applications" title="Applications: connect your software">
        <p>
          Register every piece of software that signs users in or calls your APIs: your website, your mobile app, a nightly
          job. Each registration gets a <strong>client ID</strong> that your code uses to talk to this environment&apos;s
          sign-in service.
        </p>
        <ul>
          <li>Your users&apos; accounts live in Dovepeak, in this <strong>{environment.kind}</strong> environment. Your app never sees or stores passwords.</li>
          <li>After registering, open the application and go to its <strong>Integration</strong> tab: it lists everything to copy into your code.</li>
          <li>The other tabs on this page define permissions for your APIs and show the people who have signed up.</li>
        </ul>
      </PageGuide>
      <Card>
        {applications.length === 0 ? <Empty>No applications in this environment yet. Register your first one below.</Empty> : (
          <table>
            <thead><tr><th>Name</th><th>Type</th><th>Client ID</th><th>Scopes</th></tr></thead>
            <tbody>
              {applications.map((a) => (
                <tr key={a.id}>
                  <td><Link href={`${base}/applications/${a.id}`}>{a.name}</Link></td>
                  <td>{KIND_LABEL[a.kind] ?? a.kind}</td>
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
            <fieldset>
              <legend>What kind of app is it?</legend>
              <div className="choices">
                {KINDS.map((k) => (
                  <label key={k.value} className="choice">
                    <input type="radio" name="kind" value={k.value} defaultChecked={k.value === "web"} />
                    <strong>{k.label}</strong>
                    <span className="small muted">{k.description}</span>
                  </label>
                ))}
              </div>
            </fieldset>
            <label>Callback URLs <span className="hint">Where users come back to after signing in: an address in your app that finishes the sign-in. One per line, exact match, no wildcards; HTTPS except for localhost. Examples: http://localhost:3000/api/auth/callback (Next.js SDK), com.example.app:/callback (mobile). Leave empty for machine-to-machine services.</span>
              <textarea name="redirectUris" placeholder="http://localhost:3000/api/auth/callback" />
            </label>
            <label>Logout URLs <span className="hint">Pages users may land on after signing out, usually your home page.</span>
              <textarea name="postLogoutRedirectUris" placeholder="http://localhost:3000/" />
            </label>
            <label>Allowed origins (CORS) <span className="hint">Single-page apps only: the address your app is served from, such as http://localhost:5173, so the browser may call the sign-in service.</span>
              <textarea name="webOrigins" />
            </label>
            <label>API audiences <span className="hint">Identifiers of your own APIs this app calls, such as orders-api. Tokens carry them in the aud claim, and each API checks for its own identifier.</span>
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
