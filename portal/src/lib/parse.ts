/** Form parsing helpers shared by server actions. */

export function text(form: FormData, name: string): string {
  return String(form.get(name) ?? "").trim();
}

/** Optional text: empty becomes undefined so the API applies its default. */
export function optionalText(form: FormData, name: string): string | undefined {
  return text(form, name) || undefined;
}

/** One value per line (commas also accepted), trimmed, empty entries dropped. */
export function list(form: FormData, name: string): string[] {
  return text(form, name).split(/[\n,]/).map((v) => v.trim()).filter(Boolean);
}

/** Checked checkbox values with the given name. */
export function checked(form: FormData, name: string): string[] {
  return form.getAll(name).map(String);
}

/** Whole seconds, or null when the field is empty (inherit the default). */
export function seconds(form: FormData, name: string): number | null {
  const value = text(form, name);
  if (!value) return null;
  const parsed = Number(value);
  return Number.isInteger(parsed) ? parsed : NaN;
}
