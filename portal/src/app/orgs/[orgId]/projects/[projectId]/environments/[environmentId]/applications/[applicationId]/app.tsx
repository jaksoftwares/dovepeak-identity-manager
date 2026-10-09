import { PageHeader, Tabs } from "@/components/ui";
import { envPath, orgPath, type Application, type Environment, type Organization, type Project } from "@/lib/api";
import { load } from "@/lib/load";

export type AppParams = Promise<{ orgId: string; projectId: string; environmentId: string; applicationId: string }>;
export type AppSection = "settings" | "credentials" | "roles" | "integration";

/** Loads an application with its context and renders the shared header and tabs. */
export async function applicationContext(params: AppParams, section: AppSection) {
  const { orgId, projectId, environmentId, applicationId } = await params;
  const ids = { orgId, projectId, environmentId };
  const [org, project, environment, application] = await Promise.all([
    load<Organization>(orgPath(orgId)),
    load<Project>(orgPath(orgId, `/projects/${projectId}`)),
    load<Environment>(envPath(orgId, projectId, environmentId)),
    load<Application>(envPath(orgId, projectId, environmentId, `/applications/${applicationId}`)),
  ]);
  const envBase = `/orgs/${orgId}/projects/${projectId}/environments/${environmentId}`;
  const base = `${envBase}/applications/${applicationId}`;
  const confidential = application.kind === "web" || application.kind === "machine";

  const tabs = [
    { href: base, label: "Settings" },
    ...(confidential ? [{ href: `${base}/credentials`, label: "Credentials" }] : []),
    { href: `${base}/roles`, label: "Roles" },
    { href: `${base}/integration`, label: "Integration" },
  ];

  const header = (
    <>
      <PageHeader
        title={application.name}
        subtitle={<>{application.kind} application · client ID <code data-testid="client-id">{application.clientId}</code></>}
        crumbs={[
          { href: `/orgs/${orgId}/projects/${projectId}`, label: project.name },
          { href: envBase, label: environment.kind },
        ]}
      />
      <Tabs items={tabs} current={section === "settings" ? base : `${base}/${section}`} />
    </>
  );

  return { org, project, environment, application, ids, base, envBase, header, confidential };
}
