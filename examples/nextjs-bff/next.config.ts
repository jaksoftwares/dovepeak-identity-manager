import type { NextConfig } from "next";

const issuerOrigin = process.env.DOVEPEAK_ISSUER ? new URL(process.env.DOVEPEAK_ISSUER).origin : "";

const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  // "same-origin", not "no-referrer": with no-referrer browsers send Origin: null on form POSTs, which defeats the
  // Origin check on sign-out. Cross-site requests still carry no referrer.
  { key: "Referrer-Policy", value: "same-origin" },
  { key: "X-Frame-Options", value: "DENY" },
  {
    key: "Content-Security-Policy",
    // form-action must allow the identity provider: browsers apply it to redirects after a form POST (logout).
    value: `frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self' ${issuerOrigin}`.trim(),
  },
];

const config: NextConfig = {
  poweredByHeader: false,
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
};

export default config;
