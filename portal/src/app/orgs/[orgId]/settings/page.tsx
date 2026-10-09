import { deleteOrganization, renameOrganization } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, PageHeader } from "@/components/ui";
import { orgPath, type Organization } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Settings" };

export default async function Settings({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const owner = can.manageOrganization(org.role);

  return (
    <>
      <PageHeader title="Settings" subtitle={<>Organization ID <code>{org.id}</code></>} />
      <Card title="Name">
        {owner ? (
          <ActionForm action={renameOrganization.bind(null, orgId)} submitLabel="Save" className="inline" resetOnSuccess={false}>
            <label>Name<input name="name" defaultValue={org.name} required maxLength={200} /></label>
          </ActionForm>
        ) : (
          <p>{org.name} <span className="muted small">Only owners can rename the organization.</span></p>
        )}
      </Card>
      {owner && (
        <Card title="Delete organization">
          <p className="muted">Delete every project first. Members, invitations, API keys and webhooks are removed with the organization.</p>
          <ActionForm action={deleteOrganization.bind(null, orgId)} submitLabel="Delete organization" submitClass="danger" confirm={`Delete ${org.name}? This cannot be undone.`} />
        </Card>
      )}
    </>
  );
}
