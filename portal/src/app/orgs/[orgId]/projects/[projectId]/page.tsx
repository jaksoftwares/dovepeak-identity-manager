import Link from "next/link";
import { deleteProject, renameProject } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Badge, Card, PageHeader, When } from "@/components/ui";
import { orgPath, type Organization, type Project } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Project" };

export default async function ProjectPage({ params }: { params: Promise<{ orgId: string; projectId: string }> }) {
  const { orgId, projectId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const project = await load<Project>(orgPath(orgId, `/projects/${projectId}`));
  const provisioning = project.environments.some((e) => e.state !== "ready");

  return (
    <>
      <PageHeader
        title={project.name}
        subtitle={<>Slug <code>{project.slug}</code></>}
        crumbs={[{ href: `/orgs/${orgId}/projects`, label: "Projects" }]}
      />
      {provisioning && (
        <div className="alert warn" role="status">
          Environments are being provisioned. This usually takes a few seconds; refresh to update.
        </div>
      )}
      <Card title="Environments">
        <table>
          <thead><tr><th>Environment</th><th>State</th><th>Issuer</th><th>Created</th></tr></thead>
          <tbody>
            {project.environments.map((e) => (
              <tr key={e.id}>
                <td>
                  {e.state === "ready"
                    ? <Link href={`/orgs/${orgId}/projects/${projectId}/environments/${e.id}`} data-testid={`env-${e.kind}`}>{e.kind}</Link>
                    : e.kind}
                </td>
                <td><Badge value={e.state} /></td>
                <td className="mono small">{e.issuer}</td>
                <td><When value={e.createdAt} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
      {can.write(org.role) && (
        <Card title="Rename">
          <ActionForm action={renameProject.bind(null, orgId, projectId)} submitLabel="Save" className="inline" resetOnSuccess={false}>
            <label>Name<input name="name" defaultValue={project.name} required maxLength={200} /></label>
          </ActionForm>
        </Card>
      )}
      {can.deleteProjects(org.role) && (
        <Card title="Delete project">
          <p className="muted">Deletes all three environments and their users. Remove the project&apos;s applications first.</p>
          <ActionForm
            action={deleteProject.bind(null, orgId, projectId)}
            submitLabel="Delete project"
            submitClass="danger"
            confirm={`Delete ${project.name} and all of its environments? This cannot be undone.`}
          />
        </Card>
      )}
    </>
  );
}
