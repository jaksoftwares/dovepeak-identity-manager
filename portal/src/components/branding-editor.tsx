"use client";

import { useActionState, useState } from "react";
import { useFormStatus } from "react-dom";
import { ActionResult } from "@/components/forms";
import { initialState, type ActionState } from "@/lib/action-state";
import type { Branding } from "@/lib/api";

const PLATFORM_COLOR = "#ff6300";
const NAVY = "#000027";

function luminance(hex: string): number {
  const channel = (i: number) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

/** Same rule as the platform (TenantBranding.TextColorOn): white or navy, whichever contrasts more. */
function textOn(hex: string): string {
  const l = luminance(hex);
  return 1.05 / (l + 0.05) >= (l + 0.05) / (luminance(NAVY) + 0.05) ? "#ffffff" : NAVY;
}

const validHex = (value: string) => /^#[0-9a-fA-F]{6}$/.test(value);

export function BrandingEditor({
  projectName,
  branding,
  action,
  canEdit,
}: {
  projectName: string;
  branding: Branding;
  action: (state: ActionState, form: FormData) => Promise<ActionState>;
  canEdit: boolean;
}) {
  const [state, formAction] = useActionState(action, initialState);
  const [logoUrl, setLogoUrl] = useState(branding.logoUrl ?? "");
  const [color, setColor] = useState(branding.primaryColor ?? PLATFORM_COLOR);
  const [useDefault, setUseDefault] = useState(!branding.primaryColor);
  const [subject, setSubject] = useState(branding.emailVerificationSubject ?? "");
  const [intro, setIntro] = useState(branding.emailVerificationIntro ?? "");

  const effective = useDefault || !validHex(color) ? PLATFORM_COLOR : color.toLowerCase();
  const onEffective = useDefault ? "#ffffff" : textOn(effective);
  const showLogo = logoUrl.startsWith("https://");

  return (
    <form action={formAction} className="grid cols-2" style={{ alignItems: "start" }}>
      <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, display: "grid", gap: 16 }}>
        <section className="card" style={{ margin: 0 }}>
          <h2>Hosted pages</h2>
          <div className="stack" style={{ display: "grid", gap: 14 }}>
            <label>Logo URL <span className="hint">HTTPS only, e.g. https://cdn.example.com/logo.png. Shown above the sign-in card and in emails.</span>
              <input name="logoUrl" type="url" value={logoUrl} onChange={(e) => setLogoUrl(e.target.value)} placeholder="https://" />
            </label>
            <label className="row" style={{ display: "flex", fontWeight: 400 }}>
              <input type="checkbox" name="useDefaultColor" checked={useDefault} onChange={(e) => setUseDefault(e.target.checked)} />
              Use the Dovepeak colour
            </label>
            {!useDefault && (
              <label>Brand colour <span className="hint">Button text switches between white and navy automatically for readability.</span>
                <span className="row">
                  <input type="color" value={validHex(color) ? color : PLATFORM_COLOR} onChange={(e) => setColor(e.target.value)} aria-label="Pick brand colour" />
                  <input name="primaryColor" value={color} onChange={(e) => setColor(e.target.value)} pattern="#[0-9a-fA-F]{6}" style={{ width: 120 }} />
                </span>
              </label>
            )}
          </div>
        </section>

        <section className="card" style={{ margin: 0 }}>
          <h2>Email templates</h2>
          <p className="small muted">
            Plain text. Buttons, links, expiry notices and the &quot;Secured by Dovepeak Identity&quot; footer are always added by the
            platform, so emails stay recognisable and links cannot be altered. Leave a field empty for the default.
          </p>
          <div style={{ display: "grid", gap: 14 }}>
            <h3>Email verification</h3>
            <label>Subject<input name="emailVerificationSubject" maxLength={150} value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Verify your email address" /></label>
            <label>Introduction<textarea name="emailVerificationIntro" maxLength={1000} value={intro} onChange={(e) => setIntro(e.target.value)} placeholder={`Thanks for creating a ${projectName} account. Please confirm that this is your email address.`} style={{ fontFamily: "inherit" }} /></label>
            <h3>Password reset</h3>
            <label>Subject<input name="passwordResetSubject" maxLength={150} defaultValue={branding.passwordResetSubject ?? ""} placeholder="Reset your password" /></label>
            <label>Introduction<textarea name="passwordResetIntro" maxLength={1000} defaultValue={branding.passwordResetIntro ?? ""} placeholder={`We received a request to reset the password for your ${projectName} account.`} style={{ fontFamily: "inherit" }} /></label>
            <p className="small muted">Security alerts (password and sign-in method changes) use the same branded layout automatically.</p>
          </div>
        </section>

        {canEdit && (
          <div>
            <Save />
            <ActionResult state={state} />
          </div>
        )}
      </fieldset>

      <div style={{ display: "grid", gap: 16, position: "sticky", top: 16 }}>
        <section className="card" style={{ margin: 0 }} aria-label="Sign-in page preview">
          <h2>Sign-in page preview</h2>
          <div style={{ background: "radial-gradient(circle at 20% 10%, #1a1a4d 0%, #000027 60%)", borderRadius: 10, padding: "24px 20px", textAlign: "center" }}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            {showLogo && <img src={logoUrl} alt="" style={{ maxHeight: 48, maxWidth: 200, display: "block", margin: "0 auto 10px" }} />}
            <div style={{ color: "#f8f8ff", fontSize: 22, fontWeight: 600, marginBottom: 14 }}>{projectName}</div>
            <div style={{ background: "#fff", borderRadius: 10, borderTop: `5px solid ${effective}`, padding: 18, textAlign: "left" }}>
              <div style={{ fontWeight: 600, marginBottom: 10 }}>Sign in to your account</div>
              <div style={{ border: "1px solid #c9c9dc", borderRadius: 6, height: 30, marginBottom: 8 }} />
              <div style={{ border: "1px solid #c9c9dc", borderRadius: 6, height: 30, marginBottom: 12 }} />
              <div data-testid="preview-button" style={{ background: effective, color: onEffective, borderRadius: 6, padding: "8px 0", textAlign: "center", fontWeight: 600 }}>Sign In</div>
            </div>
          </div>
        </section>

        <section className="card" style={{ margin: 0 }} aria-label="Verification email preview">
          <h2>Verification email preview</h2>
          <div className="small muted">Subject: <strong>{subject || "Verify your email address"}</strong></div>
          <div style={{ border: "1px solid #e2e2ef", borderTop: `5px solid ${effective}`, borderRadius: 8, padding: 16, marginTop: 8 }}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            {showLogo && <img src={logoUrl} alt="" style={{ maxHeight: 36, maxWidth: 160, display: "block", marginBottom: 8 }} />}
            <div style={{ fontWeight: 600, marginBottom: 8 }}>{projectName}</div>
            <p style={{ whiteSpace: "pre-wrap" }}>{intro || `Thanks for creating a ${projectName} account. Please confirm that this is your email address.`}</p>
            <span style={{ background: effective, color: onEffective, padding: "8px 14px", borderRadius: 6, fontWeight: 600, display: "inline-block" }}>Verify email address</span>
            <p className="small muted" style={{ marginTop: 12, marginBottom: 0 }}>Secured by Dovepeak Identity</p>
          </div>
        </section>
      </div>
    </form>
  );
}

function Save() {
  const { pending } = useFormStatus();
  return <button type="submit" disabled={pending}>{pending ? "Saving…" : "Save branding"}</button>;
}
