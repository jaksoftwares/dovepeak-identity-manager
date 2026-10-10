"use server";

// Server actions. Each one calls the Management API as the signed-in developer, so authorization is enforced by
// the API itself (roles, tenant isolation); the portal never decides access on its own.

import { revalidatePath } from "next/cache";
import { redirect, unstable_rethrow } from "next/navigation";
import type { ActionState } from "@/lib/action-state";
import {
  api, ApiError, envPath, orgPath,
  type Application, type Organization, type Project,
} from "@/lib/api";
import { checked, list, optionalText, seconds, text } from "@/lib/parse";

async function run(work: () => Promise<ActionState | void>): Promise<ActionState> {
  try {
    const result = (await work()) ?? {};
    revalidatePath("/", "layout");
    return { ok: true, ...result };
  } catch (error) {
    unstable_rethrow(error);
    if (error instanceof ApiError) return { error: error.display };
    console.error("Portal action failed", error instanceof Error ? error.message : error);
    return { error: "Something went wrong. Please try again." };
  }
}

// ---------------------------------------------------------------- Organizations and membership

export async function createOrganization(_: ActionState, form: FormData): Promise<ActionState> {
  let created: Organization | undefined;
  const result = await run(async () => {
    created = await api<Organization>("POST", "/v1/organizations", {
      body: { slug: text(form, "slug"), name: text(form, "name") },
      idempotent: true,
    });
  });
  if (created) redirect(`/orgs/${created.id}`);
  return result;
}

export async function acceptInvitation(invitationId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("POST", `/v1/me/invitations/${invitationId}/accept`);
    return { message: "Invitation accepted." };
  });
}

export async function renameOrganization(orgId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("PATCH", orgPath(orgId), { body: { name: text(form, "name") } });
    return { message: "Organization renamed." };
  });
}

export async function deleteOrganization(orgId: string, _: ActionState): Promise<ActionState> {
  const result = await run(async () => {
    await api("DELETE", orgPath(orgId));
  });
  if (result.ok) redirect("/orgs");
  return result;
}

export async function inviteMember(orgId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("POST", orgPath(orgId, "/invitations"), {
      body: { email: text(form, "email"), role: text(form, "role") },
      idempotent: true,
    });
    return { message: `Invitation sent to ${text(form, "email")}.` };
  });
}

export async function revokeInvitation(orgId: string, invitationId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", orgPath(orgId, `/invitations/${invitationId}`));
    return { message: "Invitation revoked." };
  });
}

export async function changeMemberRole(orgId: string, userId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("PATCH", orgPath(orgId, `/members/${userId}`), { body: { role: text(form, "role") } });
    return { message: "Role updated." };
  });
}

export async function removeMember(orgId: string, userId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", orgPath(orgId, `/members/${userId}`));
    return { message: "Member removed." };
  });
}

// ---------------------------------------------------------------- Projects

export async function createProject(orgId: string, _: ActionState, form: FormData): Promise<ActionState> {
  let created: Project | undefined;
  const result = await run(async () => {
    created = await api<Project>("POST", orgPath(orgId, "/projects"), {
      body: { slug: text(form, "slug"), name: text(form, "name") },
      idempotent: true,
    });
  });
  if (created) redirect(`/orgs/${orgId}/projects/${created.id}`);
  return result;
}

export async function renameProject(orgId: string, projectId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("PATCH", orgPath(orgId, `/projects/${projectId}`), { body: { name: text(form, "name") } });
    return { message: "Project renamed." };
  });
}

export async function deleteProject(orgId: string, projectId: string, _: ActionState): Promise<ActionState> {
  const result = await run(async () => {
    await api("DELETE", orgPath(orgId, `/projects/${projectId}`));
  });
  if (result.ok) redirect(`/orgs/${orgId}/projects`);
  return result;
}

export async function updateBranding(orgId: string, projectId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("PUT", orgPath(orgId, `/projects/${projectId}/branding`), {
      body: {
        logoUrl: optionalText(form, "logoUrl") ?? null,
        primaryColor: form.get("useDefaultColor") === "on" ? null : optionalText(form, "primaryColor") ?? null,
        emailVerificationSubject: optionalText(form, "emailVerificationSubject") ?? null,
        emailVerificationIntro: optionalText(form, "emailVerificationIntro") ?? null,
        passwordResetSubject: optionalText(form, "passwordResetSubject") ?? null,
        passwordResetIntro: optionalText(form, "passwordResetIntro") ?? null,
      },
    });
    return { message: "Branding saved and applied to every environment." };
  });
}

// ---------------------------------------------------------------- Scopes

type Env = { orgId: string; projectId: string; environmentId: string };

export async function createScope(env: Env, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("POST", envPath(env.orgId, env.projectId, env.environmentId, "/scopes"), {
      body: { name: text(form, "name"), description: optionalText(form, "description") },
      idempotent: true,
    });
    return { message: `Scope ${text(form, "name")} created.` };
  });
}

export async function deleteScope(env: Env, name: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", envPath(env.orgId, env.projectId, env.environmentId, `/scopes/${encodeURIComponent(name)}`));
    return { message: `Scope ${name} deleted.` };
  });
}

// ---------------------------------------------------------------- Applications

function tokenPolicy(form: FormData) {
  return {
    accessTokenLifetimeSeconds: seconds(form, "accessTokenLifetimeSeconds"),
    sessionIdleTimeoutSeconds: seconds(form, "sessionIdleTimeoutSeconds"),
    sessionMaxLifetimeSeconds: seconds(form, "sessionMaxLifetimeSeconds"),
  };
}

export async function createApplication(env: Env, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const kind = text(form, "kind");
    const interactive = kind !== "machine";
    const created = await api<{ application: Application; clientSecret?: string | null }>(
      "POST",
      envPath(env.orgId, env.projectId, env.environmentId, "/applications"),
      {
        body: {
          name: text(form, "name"),
          kind,
          redirectUris: interactive ? list(form, "redirectUris") : [],
          postLogoutRedirectUris: interactive ? list(form, "postLogoutRedirectUris") : [],
          webOrigins: interactive ? list(form, "webOrigins") : [],
          audiences: list(form, "audiences"),
          scopes: checked(form, "scopes"),
        },
        idempotent: true,
      },
    );

    return {
      message: `Application ${created.application.name} created. Client ID: ${created.application.clientId}`,
      secret: created.clientSecret
        ? { label: "Client secret", value: created.clientSecret }
        : undefined,
    };
  });
}

export async function updateApplication(env: Env, applicationId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const interactive = text(form, "kind") !== "machine";
    await api("PATCH", envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}`), {
      body: {
        name: text(form, "name"),
        ...(interactive && {
          redirectUris: list(form, "redirectUris"),
          postLogoutRedirectUris: list(form, "postLogoutRedirectUris"),
          webOrigins: list(form, "webOrigins"),
        }),
        audiences: list(form, "audiences"),
        scopes: checked(form, "scopes"),
        tokenPolicy: tokenPolicy(form),
      },
    });
    return { message: "Settings saved and applied to the identity engine." };
  });
}

export async function deleteApplication(env: Env, applicationId: string, _: ActionState): Promise<ActionState> {
  const base = `/orgs/${env.orgId}/projects/${env.projectId}/environments/${env.environmentId}`;
  const result = await run(async () => {
    await api("DELETE", envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}`));
  });
  if (result.ok) redirect(base);
  return result;
}

export async function rotateSecret(env: Env, applicationId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const revokePrevious = form.get("revokePrevious") === "on";
    const rotated = await api<{ clientSecret: string; previousSecretExpiresAt?: string | null }>(
      "POST",
      envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}/secret${revokePrevious ? "?revokePrevious=true" : ""}`),
      { idempotent: true },
    );
    return {
      message: rotated.previousSecretExpiresAt
        ? `New secret issued. The previous secret keeps working until ${new Date(rotated.previousSecretExpiresAt).toUTCString()}.`
        : "New secret issued. The previous secret no longer works.",
      secret: { label: "New client secret", value: rotated.clientSecret },
    };
  });
}

export async function revokePreviousSecret(env: Env, applicationId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}/secret/previous`));
    return { message: "The previous secret no longer works." };
  });
}

// ---------------------------------------------------------------- Roles and end users

export async function createRole(env: Env, applicationId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    await api("POST", envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}/roles`), {
      body: { name: text(form, "name"), description: optionalText(form, "description") },
      idempotent: true,
    });
    return { message: `Role ${text(form, "name")} created.` };
  });
}

export async function deleteRole(env: Env, applicationId: string, role: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}/roles/${encodeURIComponent(role)}`));
    return { message: `Role ${role} deleted.` };
  });
}

export async function assignRole(env: Env, applicationId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const role = text(form, "role");
    const userId = text(form, "userId");
    const assign = form.get("operation") !== "remove";
    await api(assign ? "PUT" : "DELETE",
      envPath(env.orgId, env.projectId, env.environmentId, `/applications/${applicationId}/roles/${encodeURIComponent(role)}/users/${encodeURIComponent(userId)}`));
    return { message: assign ? `Role ${role} assigned.` : `Role ${role} removed.` };
  });
}

export async function revokeUserSessions(env: Env, userId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", envPath(env.orgId, env.projectId, env.environmentId, `/users/${encodeURIComponent(userId)}/sessions`));
    return { message: "All sessions revoked. The user must sign in again." };
  });
}

// ---------------------------------------------------------------- API keys and webhooks

export async function createApiKey(orgId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const expires = text(form, "expiresAt");
    const created = await api<{ key: string; apiKey: { name: string } }>("POST", orgPath(orgId, "/api-keys"), {
      body: {
        name: text(form, "name"),
        scopes: checked(form, "scopes"),
        expiresAt: expires ? new Date(`${expires}T23:59:59Z`).toISOString() : null,
      },
      idempotent: true,
    });
    return { message: `API key ${created.apiKey.name} created.`, secret: { label: "API key", value: created.key } };
  });
}

export async function revokeApiKey(orgId: string, keyId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", orgPath(orgId, `/api-keys/${keyId}`));
    return { message: "API key revoked. It stops working immediately." };
  });
}

export async function createWebhook(orgId: string, _: ActionState, form: FormData): Promise<ActionState> {
  return run(async () => {
    const created = await api<{ signingSecret: string }>("POST", orgPath(orgId, "/webhooks"), {
      body: { url: text(form, "url"), eventTypes: list(form, "eventTypes") },
      idempotent: true,
    });
    return {
      message: "Webhook endpoint added.",
      secret: {
        label: "Signing secret",
        value: created.signingSecret,
        note: "Use it to verify the Dovepeak-Signature header. It will not be shown again.",
      },
    };
  });
}

export async function deleteWebhook(orgId: string, webhookId: string, _: ActionState): Promise<ActionState> {
  return run(async () => {
    await api("DELETE", orgPath(orgId, `/webhooks/${webhookId}`));
    return { message: "Webhook endpoint removed." };
  });
}
