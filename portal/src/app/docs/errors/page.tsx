export const metadata = { title: "Errors and troubleshooting" };

const CODES: [string, number, string][] = [
  ["validation_failed", 400, "A field is invalid. The errors object names each field and why."],
  ["idempotency_key_reused", 400, "The Idempotency-Key was already used with a different request body. Use a new key."],
  ["permission_denied", 403, "Your role (or the API key's scopes) does not allow this action in an organization you belong to."],
  ["slug_taken / name_taken", 409, "Another organization, project or application already uses this identifier."],
  ["environment_not_ready", 409, "The environment is still being provisioned. Retry in a few seconds."],
  ["project_not_empty / organization_not_empty", 409, "Delete the contained applications or projects first."],
  ["scope_in_use", 409, "Remove the scope from the listed applications before deleting it."],
  ["scope_exists / role_exists / already_member", 409, "The resource already exists."],
  ["last_owner", 409, "Every organization must keep at least one owner."],
  ["public_client", 409, "SPA and native applications have no client secret to rotate."],
  ["quota_exceeded", 409, "A per-organization or per-environment limit was reached."],
  ["request_in_progress", 409, "A request with the same Idempotency-Key is still running."],
  ["identity_engine_unavailable", 503, "The identity engine did not complete the change. Nothing was half-applied; retry shortly."],
];

export default function Errors() {
  return (
    <article>
      <h1>Errors and troubleshooting</h1>
      <p>
        The Management API returns RFC 9457 problem details with a stable <code>code</code> field. Resources in other
        organizations always return <code>404</code>, never <code>403</code>, so their existence is not disclosed.
      </p>
      <table>
        <thead><tr><th>Code</th><th>Status</th><th>Meaning</th></tr></thead>
        <tbody>{CODES.map(([code, status, meaning]) => <tr key={code}><td className="mono">{code}</td><td>{status}</td><td>{meaning}</td></tr>)}</tbody>
      </table>

      <h2>Sign-in problems</h2>
      <ul>
        <li><strong>&quot;Invalid parameter: redirect_uri&quot;</strong>: the callback URL must match a registered one exactly, including scheme, port and path.</li>
        <li><strong><code>invalid_scope</code></strong> from the token endpoint: the application requested a scope it has not been granted.</li>
        <li><strong>PKCE errors</strong>: send <code>code_challenge</code> with method <code>S256</code>, and the matching <code>code_verifier</code> when exchanging the code.</li>
        <li><strong>401 at your API</strong>: check the token&apos;s <code>aud</code> (add your API as an audience), <code>iss</code> (environment issuer) and expiry.</li>
        <li><strong>Refresh fails after working</strong>: refresh tokens are single-use. Two concurrent refreshes with the same token end the session; serialize refreshes.</li>
        <li><strong><code>roles</code> missing</strong>: assign the role to the user for <em>this</em> application, then sign in again.</li>
      </ul>
    </article>
  );
}
