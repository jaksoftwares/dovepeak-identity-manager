import { updateBranding } from "@/app/actions";
import { BrandingEditor } from "@/components/branding-editor";
import { PageHeader } from "@/components/ui";
import { get, orgPath, type Branding, type Organization, type Project } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Branding" };

export default async function BrandingPage({ params }: { params: Promise<{ orgId: string; projectId: string }> }) {
  const { orgId, projectId } = await params;
  const [org, project] = await Promise.all([
    load<Organization>(orgPath(orgId)),
    load<Project>(orgPath(orgId, `/projects/${projectId}`)),
  ]);
  const branding = await get<Branding>(orgPath(orgId, `/projects/${projectId}/branding`));

  return (
    <>
      <PageHeader
        title="Branding and emails"
        subtitle="Applies to the hosted sign-in pages and emails of every environment in this project."
        crumbs={[{ href: `/orgs/${orgId}/projects`, label: "Projects" }, { href: `/orgs/${orgId}/projects/${projectId}`, label: project.name }]}
      />
      <BrandingEditor
        projectName={project.name}
        branding={branding}
        action={updateBranding.bind(null, orgId, projectId)}
        canEdit={can.write(org.role)}
      />
    </>
  );
}
