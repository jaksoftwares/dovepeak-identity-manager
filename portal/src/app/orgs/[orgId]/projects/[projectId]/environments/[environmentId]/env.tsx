import { PageHeader, Tabs } from "@/components/ui";
import { envPath, orgPath, type Environment, type Organization, type Project } from "@/lib/api";
import { load } from "@/lib/load";

export type EnvParams = Promise<{ orgId: string; projectId: string; environmentId: string }>;

/** Loads the environment context shared by every environment page and renders its header and tabs. */
export async function environmentContext(params: EnvParams, section: "applications" | "scopes" | "users") {
  const { orgId, projectId, environmentId } = await params;
  const [org, project, environment] = await Promise.all([
    load<Organization>(orgPath(orgId)),
    load<Project>(orgPath(orgId, `/projects/${projectId}`)),
    load<Environment>(envPath(orgId, projectId, environmentId)),
  ]);
  const base = `/orgs/${orgId}/projects/${projectId}/environments/${environmentId}`;
  const ids = { orgId, projectId, environmentId };

  const header = (
    <>
      <PageHeader
        title={`${project.name} · ${environment.kind}`}
        subtitle={<>Issuer <code>{environment.issuer}</code></>}
        crumbs={[{ href: `/orgs/${orgId}/projects`, label: "Projects" }, { href: `/orgs/${orgId}/projects/${projectId}`, label: project.name }]}
      />
      <Tabs
        current={section === "applications" ? base : `${base}/${section}`}
        items={[
          { href: base, label: "Applications" },
          { href: `${base}/scopes`, label: "API scopes" },
          { href: `${base}/users`, label: "Users and sessions" },
        ]}
      />
    </>
  );

  return { org, project, environment, base, ids, header };
}
