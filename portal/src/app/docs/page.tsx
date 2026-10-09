import Link from "next/link";

export const metadata = { title: "Overview" };

export default function DocsHome() {
  return (
    <article>
      <h1>Dovepeak Identity documentation</h1>
      <p>
        Dovepeak Identity provides sign-in, sessions, tokens and roles for your applications through standard OpenID
        Connect and OAuth 2.0. You configure everything in this portal or through the Management API.
      </p>
      <h2>Concepts</h2>
      <ul>
        <li><strong>Organization</strong>: your team. Members have a role: viewer, developer, admin or owner.</li>
        <li><strong>Project</strong>: a product. Every project has isolated <em>development</em>, <em>staging</em> and <em>production</em> environments, each with its own users and issuer.</li>
        <li><strong>Application</strong>: an OAuth client in one environment: single-page, native, server-side web, or machine-to-machine.</li>
        <li><strong>Roles and scopes</strong>: roles describe what a <em>user</em> may do (the <code>roles</code> claim); scopes describe what a <em>token</em> may do at your APIs (the <code>scope</code> claim).</li>
      </ul>
      <h2>Start here</h2>
      <ol>
        <li><Link href="/docs/getting-started">Getting started</Link>: from account to a working sign-in in about ten minutes.</li>
        <li><Link href="/docs/nextjs">Next.js guide</Link>: the recommended backend-for-frontend pattern.</li>
        <li><Link href="/docs/protect-an-api">Protect an API</Link>: validate tokens and enforce roles and scopes.</li>
      </ol>
    </article>
  );
}
