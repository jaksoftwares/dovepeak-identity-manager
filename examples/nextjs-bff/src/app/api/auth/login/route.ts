import { NextResponse, type NextRequest } from "next/server";
import { createAuthorizationUrl } from "@/lib/oidc";
import { cookieOptions, saveTransaction, TRANSACTION_COOKIE, TRANSACTION_TTL_SECONDS } from "@/lib/session";

/** Starts Authorization Code + PKCE. ?register=1 opens the registration page instead of sign-in. */
export async function GET(request: NextRequest) {
  const register = request.nextUrl.searchParams.get("register") === "1";
  const { url, pending } = await createAuthorizationUrl({ register });
  const transactionId = await saveTransaction(pending);

  const response = NextResponse.redirect(url);
  response.cookies.set(TRANSACTION_COOKIE, transactionId, cookieOptions(TRANSACTION_TTL_SECONDS));
  return response;
}
