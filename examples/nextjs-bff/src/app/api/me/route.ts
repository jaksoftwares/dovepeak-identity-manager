import { NextResponse } from "next/server";
import { session } from "@/lib/auth";

/** The signed-in user's profile. Tokens are never returned to the browser. */
export async function GET() {
  const user = await session().getSession();
  return user
    ? NextResponse.json({ authenticated: true, user })
    : NextResponse.json({ authenticated: false }, { status: 401 });
}
