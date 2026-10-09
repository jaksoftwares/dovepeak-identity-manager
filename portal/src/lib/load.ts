import "server-only";
import { notFound } from "next/navigation";
import { ApiError, get } from "./api";

/** Loads a resource for a page. Missing (or another tenant's) resources render the 404 page. */
export async function load<T>(path: string): Promise<T> {
  try {
    return await get<T>(path);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  }
}

export type Attempt<T> = { ok: true; data: T } | { ok: false; status: number; message: string };

/** Loads data a member's role may not permit (403), so the page can explain instead of failing. */
export async function attempt<T>(path: string): Promise<Attempt<T>> {
  try {
    return { ok: true, data: await get<T>(path) };
  } catch (error) {
    if (error instanceof ApiError && (error.status === 403 || error.status === 409)) {
      return { ok: false, status: error.status, message: error.display };
    }

    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  }
}

/** Organization-level permissions per role, mirroring the Management API's matrix (for hiding unusable controls). */
export const can = {
  manageMembers: (role?: string) => role === "admin" || role === "owner",
  deleteProjects: (role?: string) => role === "admin" || role === "owner",
  manageOrganization: (role?: string) => role === "owner",
  manageApiKeys: (role?: string) => role === "admin" || role === "owner",
  readAudit: (role?: string) => role === "admin" || role === "owner",
  manageWebhooks: (role?: string) => role === "admin" || role === "owner",
  write: (role?: string) => role !== "viewer",
};

export const API_KEY_SCOPES = [
  "organization:read", "members:read", "projects:read", "projects:write", "projects:delete",
  "applications:read", "applications:write", "credentials:manage", "users:manage", "audit:read", "webhooks:manage",
] as const;
