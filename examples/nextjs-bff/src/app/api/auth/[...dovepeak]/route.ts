import { auth } from "@/lib/auth";

// Sign-in (/api/auth/login), callback, sign-out (POST /api/auth/logout) and session (/api/auth/session).
export const GET = (request: Request) => auth().GET(request);
export const POST = (request: Request) => auth().POST(request);
