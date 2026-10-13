import Link from "next/link";
import { deleteProject, renameProject } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
import { Badge, Card, Explain, PageHeader, When } from "@/components/ui";
import { orgPath, type Organization, type Project } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Project" };

const PURPOSE: Record<string, string> = {
  development: "Build and test. Use test accounts; reset freely.",
  staging: "Rehearse releases with production-like settings.",
  production: "Your real users. Register your live applications here.",
};

export default async function ProjectPage({ params }: { params: Promise<{ orgId: string; projectId: string }> }) {
  const { orgId, projectId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const project = await load<Project>(orgPath(orgId, `/projects/${projectId}`));
  const provisioning = project.environments.some((e) => e.state !== "ready");

  return (
    <>
      <PageHeader
        title={project.name}
        subtitle={<>Identifier <code>{project.slug}</code></>}
        crumbs={[{ href: `/orgs/${orgId}/projects`, label: "Projects" }]}
        actions={<Link className="button secondary" href={`/orgs/${orgId}/projects/${projectId}/branding`}>Branding and emails</Link>}
      />
      <PageGuide id="project" title="Your project's environments">
        <p>
          Each environment below is a separate, ready-to-use sign-in service for this product. <strong>Open development to
          register your first application</strong>, then copy its settings into your code.
        </p>
        <Explain
          items={[
            ["Environment", "Development, staging or production. Users, applications and settings are not shared between them."],
            ["State", "pending while Dovepeak sets the environment up (a few seconds), then ready."],
            ["Issuer", <>The address of this environment&apos;s sign-in service. Your app and APIs are configured with it, and
              every token it issues names it in the <code>iss</code> claim. Each environment has its own.</>],
          ]}
        />
        <p className="small">The button at the top right sets your logo, colours and email wording for all three environments.</p>
      </PageGuide>
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
                  <div className="small muted">{PURPOSE[e.kind]}</div>
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
