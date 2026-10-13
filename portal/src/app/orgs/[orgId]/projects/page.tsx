import Link from "next/link";
import { createProject } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { PageGuide } from "@/components/help";
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
      <PageGuide id="projects">
        <p>
          Create <strong>one project per product</strong>, for example &ldquo;Online shop&rdquo; or &ldquo;Partner app&rdquo;.
          Everything that signs in to that product lives inside it: its website, mobile app and backend services.
        </p>
        <ul>
          <li>
            Every project gets three <strong>environments</strong>: development, staging and production. They are completely
            separate: different sign-in addresses, different users and different settings. A user who registers while you
            test in development does not exist in production.
          </li>
          <li>Build and test in development. When you launch, register the same applications in production.</li>
          <li>Logo, colours and email wording are set once per project and apply to all three environments.</li>
        </ul>
      </PageGuide>
      <Card>
        {projects.length === 0 ? (
          <Empty>No projects yet. Create your first one below; it is ready in a few seconds.</Empty>
        ) : (
          <table>
            <thead><tr><th>Name</th><th>Identifier</th><th>Environments</th><th>Created</th></tr></thead>
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
            <details className="advanced">
              <summary>Advanced: choose the identifier yourself</summary>
              <label>
                Identifier (optional) <span className="hint">A short code such as shop, used by the API. Left empty, it is created for you. It cannot be changed later.</span>
                <input name="slug" pattern="[a-z][a-z0-9\-]{0,61}[a-z0-9]" placeholder="shop" />
              </label>
            </details>
          </ActionForm>
        </Card>
      )}
    </>
  );
}
