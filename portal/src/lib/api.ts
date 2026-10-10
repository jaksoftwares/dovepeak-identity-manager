import "server-only";
import { randomUUID } from "node:crypto";
import { redirect } from "next/navigation";
import { config } from "./config";
import { validAccessToken } from "./session";

/** An RFC 9457 problem returned by the Management API. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** One human-readable sentence, including field errors. */
  get display(): string {
    const fields = Object.values(this.fieldErrors).flat();
    return fields.length > 0 ? fields.join(" ") : this.message;
  }
}

interface RequestOptions {
  body?: unknown;
  /** Create requests carry an Idempotency-Key so a retried submission never creates a duplicate. */
  idempotent?: boolean;
}

/**
 * Calls the Management API as the signed-in developer. Tokens never leave the server. A missing or expired
 * session sends the browser to sign in again.
 */
export async function api<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const token = await validAccessToken();
  if (!token) {
    redirect("/api/auth/login");
  }

  const headers: Record<string, string> = { Authorization: `Bearer ${token}`, Accept: "application/json" };
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (options.idempotent) headers["Idempotency-Key"] = randomUUID();

  const response = await fetch(new URL(path, config.managementApiUrl), {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    cache: "no-store",
  });

  if (response.status === 401) {
    redirect("/api/auth/login");
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => ({}))) as {
      title?: string;
      detail?: string;
      code?: string;
      errors?: Record<string, string[]>;
    };
    throw new ApiError(response.status, problem.code, problem.detail ?? problem.title ?? `Request failed (${response.status}).`, problem.errors);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

export const get = <T>(path: string) => api<T>("GET", path);

/** Organization-relative path helper: org("/projects") → /v1/organizations/{id}/projects. */
export const orgPath = (orgId: string, suffix = "") => `/v1/organizations/${orgId}${suffix}`;

export const envPath = (orgId: string, projectId: string, environmentId: string, suffix = "") =>
  orgPath(orgId, `/projects/${projectId}/environments/${environmentId}${suffix}`);

// ---------------------------------------------------------------- Response types (mirroring the OpenAPI document)

export type OrganizationRole = "viewer" | "developer" | "admin" | "owner";
export type ApplicationKind = "spa" | "native" | "web" | "machine";

export interface Me { id: string; kind: string; email?: string | null; emailVerified?: boolean }
export interface Organization { id: string; slug: string; name: string; role?: OrganizationRole; createdAt: string }
export interface Member { userId: string; email: string; role: OrganizationRole; joinedAt: string }
export interface Invitation { id: string; organizationId: string; email: string; role: OrganizationRole; expiresAt: string; createdAt: string }
export interface Environment { id: string; projectId: string; kind: "development" | "staging" | "production"; state: string; issuer: string; createdAt: string }
export interface Project { id: string; slug: string; name: string; createdAt: string; environments: Environment[] }
export interface TokenPolicy { accessTokenLifetimeSeconds?: number | null; sessionIdleTimeoutSeconds?: number | null; sessionMaxLifetimeSeconds?: number | null }
export interface Application {
  id: string; projectId: string; environmentId: string; name: string; kind: ApplicationKind; clientId: string;
  redirectUris: string[]; postLogoutRedirectUris: string[]; webOrigins: string[]; audiences: string[]; scopes: string[];
  tokenPolicy: TokenPolicy; createdAt: string; updatedAt: string;
}
export interface ApplicationConfig {
  clientId: string; kind: ApplicationKind; issuer: string; discoveryUrl: string; tokenEndpointAuthMethod: string; pkceRequired: boolean;
  redirectUris: string[]; postLogoutRedirectUris: string[]; webOrigins: string[]; audiences: string[]; scopes: string[];
  accessTokenLifetimeSeconds: number; sessionIdleTimeoutSeconds?: number | null; sessionMaxLifetimeSeconds?: number | null;
}
export interface Branding {
  logoUrl?: string | null; primaryColor?: string | null;
  emailVerificationSubject?: string | null; emailVerificationIntro?: string | null;
  passwordResetSubject?: string | null; passwordResetIntro?: string | null;
}
export interface Scope { name: string; description?: string | null; createdAt: string }
export interface Role { name: string; description?: string | null; createdAt: string }
export interface EndUser { id: string; email?: string | null; emailVerified: boolean; enabled: boolean; createdAt: string }
export interface Session { id: string; ipAddress?: string | null; started: string; lastAccess: string; clients: string[] }
export interface ApiKey { id: string; name: string; prefix: string; scopes: string[]; createdAt: string; expiresAt?: string | null; revokedAt?: string | null; lastUsedAt?: string | null }
export interface Webhook { id: string; url: string; eventTypes: string[]; active: boolean; createdAt: string }
export interface WebhookDelivery { id: string; eventId: string; eventType: string; status: string; attempts: number; lastStatusCode?: number | null; createdAt: string; deliveredAt?: string | null }
export interface AuditEvent {
  id: string; source: string; type: string; occurredAt: string; actorType?: string | null; actorId?: string | null;
  resourceType?: string | null; resourceId?: string | null; environmentId?: string | null; clientId?: string | null;
  ipAddress?: string | null; error?: string | null; details: unknown;
}
export interface AuditPage { events: AuditEvent[]; nextBefore?: string | null }
