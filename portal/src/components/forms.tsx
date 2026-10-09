"use client";

import { useActionState, useState, type ReactNode } from "react";
import { useFormStatus } from "react-dom";
import { initialState, type ActionState } from "@/lib/action-state";

type Action = (state: ActionState, formData: FormData) => Promise<ActionState>;

/** A form bound to a server action, showing its error, success message and any one-time secret. */
export function ActionForm({
  action,
  children,
  className = "stack",
  submitLabel,
  submitClass,
  confirm,
  resetOnSuccess = true,
}: {
  action: Action;
  children?: ReactNode;
  className?: string;
  submitLabel: string;
  submitClass?: string;
  confirm?: string;
  resetOnSuccess?: boolean;
}) {
  const [state, formAction] = useActionState(action, initialState);
  const [formKey, setFormKey] = useState(0);
  const [lastState, setLastState] = useState(state);
  if (state !== lastState) {
    setLastState(state);
    if (state.ok && resetOnSuccess) setFormKey((k) => k + 1);
  }

  return (
    <div>
      <form
        key={formKey}
        action={formAction}
        className={className}
        onSubmit={(event) => {
          if (confirm && !window.confirm(confirm)) event.preventDefault();
        }}
      >
        {children}
        <div>
          <Submit label={submitLabel} className={submitClass} />
        </div>
      </form>
      <ActionResult state={state} />
    </div>
  );
}

function Submit({ label, className }: { label: string; className?: string }) {
  const { pending } = useFormStatus();
  return (
    <button type="submit" className={className} disabled={pending}>
      {pending ? "Working…" : label}
    </button>
  );
}

export function ActionResult({ state }: { state: ActionState }) {
  return (
    <>
      {state.error && <div className="alert error" role="alert">{state.error}</div>}
      {state.ok && state.message && <div className="alert success" role="status">{state.message}</div>}
      {state.secret && <SecretOnce {...state.secret} />}
    </>
  );
}

/** Displays a credential that will never be shown again, with a copy button. */
export function SecretOnce({ label, value, note }: { label: string; value: string; note?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="secret" data-testid="secret-once">
      <strong>{label}</strong>
      <code data-testid="secret-value">{value}</code>
      <div className="row">
        <button
          type="button"
          className="secondary"
          onClick={async () => {
            await navigator.clipboard.writeText(value);
            setCopied(true);
          }}
        >
          {copied ? "Copied" : "Copy"}
        </button>
        <span className="small muted">
          {note ?? "Copy it now and store it securely. It will not be shown again."}
        </span>
      </div>
    </div>
  );
}
