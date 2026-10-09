import { revokeUserSessions } from "@/app/actions";
import { ActionForm } from "@/components/forms";
import { Card, Empty, When } from "@/components/ui";
import { envPath, type EndUser, type Session } from "@/lib/api";
import { attempt } from "@/lib/load";
import { environmentContext, type EnvParams } from "../env";

export const metadata = { title: "Users and sessions" };

export default async function Users({ params, searchParams }: { params: EnvParams; searchParams: Promise<{ email?: string; user?: string }> }) {
  const { ids, base, header } = await environmentContext(params, "users");
  const { email = "", user } = await searchParams;
  const path = (suffix: string) => envPath(ids.orgId, ids.projectId, ids.environmentId, suffix);

  const users = await attempt<EndUser[]>(path(`/users?${new URLSearchParams({ ...(email && { email }), max: "50" })}`));
  const sessions = user ? await attempt<Session[]>(path(`/users/${encodeURIComponent(user)}/sessions`)) : undefined;

  return (
    <>
      {header}
      <Card>
        <form className="inline" method="get" style={{ marginBottom: 16 }}>
          <label>Email<input name="email" type="email" defaultValue={email} placeholder="user@example.com" /></label>
          <button type="submit" className="secondary">Search</button>
        </form>
        {!users.ok ? <p className="muted">{users.message}</p> : users.data.length === 0 ? <Empty>No users found.</Empty> : (
          <table>
            <thead><tr><th>Email</th><th>User ID</th><th>Verified</th><th>Enabled</th><th>Created</th><th /></tr></thead>
            <tbody>
              {users.data.map((u) => (
                <tr key={u.id}>
                  <td>{u.email ?? "—"}</td>
                  <td className="mono small">{u.id}</td>
                  <td>{u.emailVerified ? "yes" : "no"}</td>
                  <td>{u.enabled ? "yes" : "no"}</td>
                  <td><When value={u.createdAt} /></td>
                  <td><a href={`${base}/users?${new URLSearchParams({ ...(email && { email }), user: u.id })}`}>Sessions</a></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      {user && sessions && (
        <Card title="Active sessions">
          {!sessions.ok ? <p className="muted">{sessions.message}</p> : sessions.data.length === 0 ? <Empty>No active sessions.</Empty> : (
            <>
              <table>
                <thead><tr><th>Started</th><th>Last activity</th><th>IP address</th><th>Applications</th></tr></thead>
                <tbody>
                  {sessions.data.map((s) => (
                    <tr key={s.id}>
                      <td><When value={s.started} /></td><td><When value={s.lastAccess} /></td>
                      <td>{s.ipAddress ?? "—"}</td><td className="mono small">{s.clients.join(", ")}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <div style={{ marginTop: 12 }}>
                <ActionForm action={revokeUserSessions.bind(null, ids, user)} submitLabel="Revoke all sessions" submitClass="danger" confirm="Sign this user out everywhere?" />
              </div>
            </>
          )}
        </Card>
      )}
    </>
  );
}
