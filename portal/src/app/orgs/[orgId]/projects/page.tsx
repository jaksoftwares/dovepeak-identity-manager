import Link from "next/link";
import { createProject } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Badge, Card, Empty, PageHeader, When } from "@/components/ui";
import { get, orgPath, type Organization, type Project } from "@/lib/api";
import { can, load } from "@/lib/load";

export const metadata = { title: "Projects" };

export default async function Projects({ params }: { params: Promise<{ orgId: string }> }) {
  const { orgId } = await params;
  const org = await load<Organization>(orgPath(orgId));
  const projects = await get<Project[]>(orgPath(orgId, "/projects"));

  return (
    <>
      <PageHeader title="Projects" subtitle="Each project gets isolated development, staging and production environments." />
      <Card>
        {projects.length === 0 ? (
          <Empty>No projects yet.</Empty>
        ) : (
          <table>
            <thead><tr><th>Name</th><th>Slug</th><th>Environments</th><th>Created</th></tr></thead>
            <tbody>
              {projects.map((p) => (
                <tr key={p.id}>
                  <td><Link href={`/orgs/${orgId}/projects/${p.id}`}>{p.name}</Link></td>
                  <td className="mono">{p.slug}</td>
                  <td className="row">{p.environments.map((e) => <span key={e.id}>{e.kind} <Badge value={e.state} /></span>)}</td>
                  <td><When value={p.createdAt} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      {can.write(org.role) && (
        <Card title="Create a project">
          <ActionForm action={createProject.bind(null, orgId)} submitLabel="Create project">
            <label>Name<input name="name" required maxLength={200} placeholder="Online shop" /></label>
            <label>
              Slug <span className="hint">Lowercase letters, digits and hyphens; unique in the organization.</span>
              <input name="slug" required pattern="[a-z][a-z0-9\-]{0,61}[a-z0-9]" placeholder="shop" />
            </label>
          </ActionForm>
        </Card>
      )}
    </>
  );
}
