import { config } from "@/lib/config";

export const metadata = { title: "Management API reference" };

interface Operation { summary?: string; tags?: string[]; parameters?: { name: string; in: string; required?: boolean }[]; requestBody?: unknown }
interface OpenApi { info: { title: string; version: string }; paths: Record<string, Record<string, Operation>> }

const METHODS = ["get", "post", "put", "patch", "delete"] as const;

/** Rendered from the live OpenAPI document, so the reference always matches the deployed API. */
export default async function ApiReference() {
  let doc: OpenApi | undefined;
  try {
    const response = await fetch(new URL("/openapi/v1.json", config.managementApiUrl), { cache: "no-store" });
    if (response.ok) doc = (await response.json()) as OpenApi;
  } catch {
    doc = undefined;
  }

  if (!doc) {
    return <article><h1>Management API reference</h1><div className="alert error">The API reference is temporarily unavailable.</div></article>;
  }

  const groups = new Map<string, { method: string; path: string; op: Operation }[]>();
  for (const [path, item] of Object.entries(doc.paths)) {
    for (const method of METHODS) {
      const op = item[method];
      if (!op) continue;
      const tag = op.tags?.[0] ?? "Other";
      groups.set(tag, [...(groups.get(tag) ?? []), { method, path, op }]);
    }
  }

  return (
    <article>
      <h1>Management API reference</h1>
      <p>
        {doc.info.title} {doc.info.version}. Authenticate with a developer access token or an organization API key:
        <code> Authorization: Bearer …</code>. The machine-readable document is at <code>/openapi/v1.json</code> on the API.
      </p>
      {[...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([tag, operations]) => (
        <section key={tag}>
          <h2>{tag}</h2>
          <table>
            <tbody>
              {operations.map(({ method, path, op }) => (
                <tr key={`${method} ${path}`}>
                  <td style={{ width: 80 }}><span className="badge">{method.toUpperCase()}</span></td>
                  <td className="mono small">{path}</td>
                  <td className="small muted">
                    {op.summary ?? ""}
                    {(op.parameters ?? []).filter((p) => p.in === "query").map((p) => <div key={p.name}>?{p.name}</div>)}
                    {op.requestBody ? <div>JSON body</div> : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
    </article>
  );
}
