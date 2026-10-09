import { NextResponse, type NextRequest } from "next/server";
import { config } from "@/lib/config";
import { endSessionUrl, revokeSession } from "@/lib/oidc";
import { deleteSession, SESSION_COOKIE } from "@/lib/session";

/**
 * Logout is POST-only and checks the Origin header, so another site cannot log users out (CSRF).
 * It revokes the session server-side first, then ends the identity provider's browser session.
 */
export async function POST(request: NextRequest) {
  if (request.headers.get("origin") !== config.appUrl.origin) {
    return new NextResponse("Forbidden", { status: 403 });
  }

  const sessionId = request.cookies.get(SESSION_COOKIE)?.value;
  const session = sessionId ? await deleteSession(sessionId) : null;

  if (session) {
    await revokeSession(session.tokens.refreshToken);
  }

  const target = (session && (await endSessionUrl(session.tokens.idToken))) ?? new URL("/", config.appUrl);
  const response = NextResponse.redirect(target, { status: 303 });
  response.cookies.delete(SESSION_COOKIE);
  return response;
}
