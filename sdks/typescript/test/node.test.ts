import * as jose from "jose";
import { describe, expect, it } from "vitest";
import { createTokenVerifier, errorResponse, ForbiddenError, hasRoles, hasScopes, requirePermissions, TokenVerificationError } from "../src/node/index.js";
import { createFakeIssuer } from "./fake-issuer.js";

// Every invalid-token case from milestone M1.3 (TokenValidationTests) must be rejected.
describe("createTokenVerifier", () => {
  const setup = async () => {
    const fake = await createFakeIssuer();
    const verifier = createTokenVerifier({ issuer: fake.issuer, audience: "orders-api", jwks: fake.jwks, fetch: fake.fetch });
    const valid = await fake.sign({ sub: "user-1", aud: "orders-api", azp: "app_1", scope: "openid orders:read", roles: ["administrator"] });
    return { fake, verifier, valid };
  };

  const reason = async (promise: Promise<unknown>) => {
    const error = await promise.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(TokenVerificationError);
    return (error as TokenVerificationError).reason;
  };

  it("accepts a valid token and exposes scopes and roles", async () => {
    const { verifier, valid } = await setup();
    const token = await verifier.verify(valid);
    expect(token).toMatchObject({ subject: "user-1", clientId: "app_1", scopes: ["openid", "orders:read"], roles: ["administrator"] });
    expect(hasScopes(token, "orders:read")).toBe(true);
    expect(hasRoles(token, "administrator")).toBe(true);
  });

  it("rejects an expired token", async () => {
    const { fake, verifier } = await setup();
    const expired = await new jose.SignJWT({ sub: "u", aud: "orders-api" })
      .setProtectedHeader({ alg: "RS256", kid: "key-1" }).setIssuer(fake.issuer)
      .setIssuedAt(Math.floor(Date.now() / 1000) - 3600).setExpirationTime(Math.floor(Date.now() / 1000) - 600)
      .sign(fake.privateKey);
    expect(await reason(verifier.verify(expired))).toBe("expired");
  });

  it("rejects a token from another tenant (issuer)", async () => {
    const { fake, verifier } = await setup();
    const other = await new jose.SignJWT({ sub: "u", aud: "orders-api" })
      .setProtectedHeader({ alg: "RS256", kid: "key-1" }).setIssuer("https://id.example.test/realms/dp-other")
      .setIssuedAt().setExpirationTime("10m").sign(fake.privateKey);
    expect(await reason(verifier.verify(other))).toBe("issuer");
  });

  it("rejects a token issued for another API (audience)", async () => {
    const { fake, verifier } = await setup();
    expect(await reason(verifier.verify(await fake.sign({ sub: "u", aud: "billing-api" })))).toBe("audience");
  });

  it("rejects a tampered payload", async () => {
    const { verifier, valid } = await setup();
    const [header, payload, signature] = valid.split(".");
    const claims = JSON.parse(Buffer.from(payload!, "base64url").toString());
    claims.roles = ["administrator", "superuser"];
    const tampered = `${header}.${Buffer.from(JSON.stringify(claims)).toString("base64url")}.${signature}`;
    expect(await reason(verifier.verify(tampered))).toBe("signature");
  });

  it("rejects an unsigned token (alg none)", async () => {
    const { fake, verifier } = await setup();
    const unsigned = new jose.UnsecuredJWT({ sub: "u", aud: "orders-api" }).setIssuer(fake.issuer).setIssuedAt().setExpirationTime("10m").encode();
    expect(["malformed", "algorithm", "signature"]).toContain(await reason(verifier.verify(unsigned)));
  });

  it("rejects HMAC algorithm confusion using the public key as secret", async () => {
    const { fake, verifier } = await setup();
    const publicPem = await jose.exportSPKI(fake.publicKey);
    const forged = await new jose.SignJWT({ sub: "u", aud: "orders-api" })
      .setProtectedHeader({ alg: "HS256", kid: "key-1" }).setIssuer(fake.issuer).setIssuedAt().setExpirationTime("10m")
      .sign(new TextEncoder().encode(publicPem));
    expect(["algorithm", "signature"]).toContain(await reason(verifier.verify(forged)));
  });

  it("rejects a token signed by an unknown key", async () => {
    const { fake, verifier } = await setup();
    const { privateKey } = await jose.generateKeyPair("RS256");
    const foreign = await new jose.SignJWT({ sub: "u", aud: "orders-api" })
      .setProtectedHeader({ alg: "RS256", kid: "attacker" }).setIssuer(fake.issuer).setIssuedAt().setExpirationTime("10m").sign(privateKey);
    expect(await reason(verifier.verify(foreign))).toBe("signature");
  });

  it("rejects missing and malformed Authorization headers", async () => {
    const { verifier, valid } = await setup();
    expect(await reason(verifier.verifyAuthorizationHeader(undefined))).toBe("malformed");
    expect(await reason(verifier.verifyAuthorizationHeader(`Basic ${valid}`))).toBe("malformed");
    await expect(verifier.verifyAuthorizationHeader(`Bearer ${valid}`)).resolves.toMatchObject({ subject: "user-1" });
  });

  it("rejects a revoked token in introspection mode", async () => {
    const { fake, valid } = await setup();
    const verifier = createTokenVerifier({
      issuer: fake.issuer, audience: "orders-api", jwks: fake.jwks, fetch: fake.fetch,
      introspection: { clientId: "orders-api", clientSecret: "s" },
    });
    await expect(verifier.verify(valid)).resolves.toBeDefined();
    fake.state.active = false;
    expect(await reason(verifier.verify(valid))).toBe("inactive");
  });

  it("refuses unsafe configuration", () => {
    expect(() => createTokenVerifier({ issuer: "https://x", audience: [] })).toThrow();
    expect(() => createTokenVerifier({ issuer: "https://x", audience: "a", algorithms: ["HS256"] })).toThrow();
  });

  it("maps missing permissions to 403 and invalid tokens to 401", async () => {
    const { verifier, valid } = await setup();
    const token = await verifier.verify(valid);
    expect(() => requirePermissions(token, { scopes: ["orders:read"], roles: ["administrator"] })).not.toThrow();

    const error = (() => {
      try {
        requirePermissions(token, { scopes: ["orders:write"] });
      } catch (e) {
        return e;
      }
    })();
    expect(error).toBeInstanceOf(ForbiddenError);
    expect(errorResponse(error).status).toBe(403);
    expect(errorResponse(new TokenVerificationError("expired", "x")).status).toBe(401);
  });
});
