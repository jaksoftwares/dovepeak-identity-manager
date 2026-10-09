import { notFound } from "next/navigation";
import { revokePreviousSecret, rotateSecret } from "@/app/actions";
import { ActionForm } from "@/components/forms";
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
