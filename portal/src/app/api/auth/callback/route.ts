import { NextResponse, type NextRequest } from "next/server";
import { config } from "@/lib/config";
import { completeAuthorization } from "@/lib/oidc";
import { cookieOptions, createSession, SESSION_COOKIE, takeTransaction, TRANSACTION_COOKIE } from "@/lib/session";

export async function GET(request: NextRequest) {
  const transactionId = request.cookies.get(TRANSACTION_COOKIE)?.value;
  const pending = transactionId ? await takeTransaction(transactionId) : null;
  if (!pending) {
    return failure("expired");
  }

  // The callback URL must be evaluated against the public app URL, not an internal host.
  const callbackUrl = new URL(request.nextUrl.pathname + request.nextUrl.search, config.appUrl);

  try {
    const { tokens, user } = await completeAuthorization(callbackUrl, pending);
    const sessionId = await createSession({ user, tokens });

    const response = NextResponse.redirect(new URL(pending.returnTo, config.appUrl));
    response.cookies.set(SESSION_COOKIE, sessionId, cookieOptions(tokens.refreshTokenExpiresInSeconds));
    response.cookies.delete(TRANSACTION_COOKIE);
    return response;
  } catch {
    // Details are deliberately not shown: they can include identity provider error descriptions.
    return failure("failed");
  }
}

function failure(reason: string) {
  const url = new URL("/", config.appUrl);
  url.searchParams.set("error", reason);
  const response = NextResponse.redirect(url);
  response.cookies.delete(TRANSACTION_COOKIE);
  return response;
}
