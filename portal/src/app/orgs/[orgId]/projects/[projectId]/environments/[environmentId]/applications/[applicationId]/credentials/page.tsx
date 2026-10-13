import { notFound } from "next/navigation";
import { revokePreviousSecret, rotateSecret } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
import { Card } from "@/components/ui";
import { can } from "@/lib/load";
import { applicationContext, type AppParams } from "../app";

export const metadata = { title: "Credentials" };

export default async function Credentials({ params }: { params: AppParams }) {
  const { org, application, ids, header, confidential } = await applicationContext(params, "credentials");
  if (!confidential) notFound();
  const write = can.write(org.role);

  return (
    <>
      {header}
      <PageGuide id="credentials" title="The client secret: your app's password">
        <p>
          The client secret proves to Dovepeak that a request really comes from your server. Treat it like a password:
        </p>
        <ul>
          <li>Keep it on the server only, in an environment variable or secret manager. Never in browser code, mobile apps or Git.</li>
          <li>It was shown once, when the application was created. Dovepeak cannot show it again.</li>
          <li>
            <strong>Changing it without downtime:</strong> issue a new secret, deploy it, then revoke the previous one. Both keep
            working for 24 hours in between.
          </li>
          <li><strong>If it leaked:</strong> issue a new one with &ldquo;stop accepting the current secret immediately&rdquo; ticked.</li>
        </ul>
      </PageGuide>
      <Card title="Client secret">
        <p>
          Secrets are shown once, when they are issued. Dovepeak never stores them in readable form, so they cannot be
          viewed again. If a secret is lost, issue a new one.
        </p>
        {write ? (
          <ActionForm action={rotateSecret.bind(null, ids, application.id)} submitLabel="Issue a new secret" confirm="Issue a new client secret?">
            <label className="row" style={{ display: "flex", fontWeight: 400 }}>
              <input type="checkbox" name="revokePrevious" />
              Stop accepting the current secret immediately (use this if it leaked). Otherwise it keeps working for 24 hours
              so you can redeploy without downtime.
            </label>
          </ActionForm>
        ) : <p className="muted">Developers, admins and owners can rotate secrets.</p>}
      </Card>
      {write && (
        <Card title="End the overlap period">
          <p className="muted">After deploying the new secret everywhere, stop accepting the previous one.</p>
          <ActionForm action={revokePreviousSecret.bind(null, ids, application.id)} submitLabel="Revoke previous secret" submitClass="danger" confirm="Stop accepting the previous secret now?" />
        </Card>
      )}
    </>
  );
}
