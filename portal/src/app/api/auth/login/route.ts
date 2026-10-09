import { NextResponse, type NextRequest } from "next/server";
import { createAuthorizationUrl } from "@/lib/oidc";
import { cookieOptions, saveTransaction, TRANSACTION_COOKIE, TRANSACTION_TTL_SECONDS } from "@/lib/session";

/** Starts Authorization Code + PKCE against the platform realm. ?register=1 opens account creation instead. */
export async function GET(request: NextRequest) {
  const register = request.nextUrl.searchParams.get("register") === "1";
  const returnTo = safeReturnTo(request.nextUrl.searchParams.get("returnTo"));
  const { url, pending } = await createAuthorizationUrl({ register, returnTo });
  const transactionId = await saveTransaction(pending);

  const response = NextResponse.redirect(url);
  response.cookies.set(TRANSACTION_COOKIE, transactionId, cookieOptions(TRANSACTION_TTL_SECONDS));
  return response;
}

/** Only same-site paths: an open redirect after sign-in would be a phishing aid. */
function safeReturnTo(value: string | null): string {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\") ? value : "/orgs";
}
