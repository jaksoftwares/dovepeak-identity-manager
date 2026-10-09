import { NextResponse } from "next/server";
import { currentSession } from "@/lib/session";

/** The signed-in user's profile. Tokens are never returned to the browser. */
export async function GET() {
  const current = await currentSession();
  if (!current) {
    return NextResponse.json({ authenticated: false }, { status: 401 });
  }

  return NextResponse.json({ authenticated: true, user: current.session.user });
}
