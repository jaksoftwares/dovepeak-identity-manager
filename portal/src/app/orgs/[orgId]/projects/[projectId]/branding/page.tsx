import { updateBranding } from "@/app/actions";
import { BrandingEditor } from "@/components/branding-editor";
import { PageGuide } from "@/components/help";
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
      <PageGuide id="branding" title="What your users see, and which emails they get">
        <p>
          Your users never see a Dovepeak-branded product. They see <strong>your</strong> logo and colour on the sign-in,
          registration and password-reset pages, and in these emails, which Dovepeak sends for you:
        </p>
        <ul>
          <li><strong>Verify your email</strong>: right after registration. Users must click the link before their first sign-in.</li>
          <li><strong>Reset your password</strong>: when a user clicks &ldquo;Forgot password&rdquo;. The link works once, for 15 minutes.</li>
          <li><strong>Security alerts</strong>: when a user&apos;s password or sign-in methods change, or the account is locked after repeated wrong passwords.</li>
        </ul>
        <p className="small">
          You choose the subject and opening text. The link, button and &ldquo;Secured by Dovepeak Identity&rdquo; footer are
          always added by the platform, so a phishing email cannot copy a real one exactly. To protect inboxes, each address
          receives at most 5 emails per hour.
        </p>
      </PageGuide>
      <BrandingEditor
        projectName={project.name}
        branding={branding}
        action={updateBranding.bind(null, orgId, projectId)}
        canEdit={can.write(org.role)}
      />
    </>
  );
}
