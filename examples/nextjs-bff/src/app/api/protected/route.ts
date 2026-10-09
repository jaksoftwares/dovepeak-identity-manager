import { NextResponse } from "next/server";
import { config } from "@/lib/config";
import { validAccessToken } from "@/lib/session";

/**
 * Calls the protected .NET API on the user's behalf. The access token is attached server-side;
 * the browser only ever sees the API's response.
 */
export async function GET() {
  const accessToken = await validAccessToken();
  if (!accessToken) {
    return NextResponse.json({ error: "not_authenticated" }, { status: 401 });
  }

  const response = await fetch(new URL("/me", config.protectedApiUrl), {
    headers: { Authorization: `Bearer ${accessToken}` },
    cache: "no-store",
  }).catch(() => null);

  if (!response) {
    return NextResponse.json({ error: "api_unreachable" }, { status: 502 });
  }

  const body = response.ok ? await response.json() : { error: `api_status_${response.status}` };
  return NextResponse.json(body, { status: response.ok ? 200 : 502 });
}
