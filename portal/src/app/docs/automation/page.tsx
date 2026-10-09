export const metadata = { title: "Automation and webhooks" };

export default function Automation() {
  return (
    <article>
      <h1>Automation and webhooks</h1>
      <h2>API keys</h2>
      <p>
        Admins create organization API keys under <strong>API keys</strong>. A key is shown once, carries explicit scopes
        that can never exceed its creator&apos;s permissions, and cannot manage members, API keys or the organization itself.
      </p>
      <pre>{`curl -H "Authorization: Bearer $DOVEPEAK_API_KEY" \\
  https://<management-api>/v1/organizations/<orgId>/projects`}</pre>
      <p>Send an <code>Idempotency-Key</code> header on create requests so a retry never creates a duplicate. A replay returns the original response, without one-time secrets.</p>

      <h2>Webhooks</h2>
      <p>Each delivery is a JSON <code>POST</code> with these headers:</p>
      <ul>
        <li><code>Dovepeak-Event-Type</code>: the event type, e.g. <code>application.created</code>.</li>
        <li><code>Dovepeak-Signature</code>: <code>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256&gt;</code> over <code>&lt;t&gt;.&lt;raw body&gt;</code>.</li>
      </ul>
      <pre>{`import { createHmac, timingSafeEqual } from "node:crypto";

export function verify(secret, header, rawBody) {
  const parts = Object.fromEntries(header.split(",").map((p) => p.split("=")));
  if (Math.abs(Date.now() / 1000 - Number(parts.t)) > 300) return false; // replay window
  const expected = createHmac("sha256", secret).update(\`\${parts.t}.\${rawBody}\`).digest("hex");
  return timingSafeEqual(Buffer.from(expected), Buffer.from(parts.v1 ?? ""));
}`}</pre>
      <p>Respond with a 2xx status quickly. Failed deliveries are retried with exponential backoff; the endpoint&apos;s delivery history shows every attempt.</p>
    </article>
  );
}
