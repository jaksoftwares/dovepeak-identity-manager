# Runbook: Signing Key Rotation

**Applies to:** every tenant realm
**Owner:** Platform operations
**Related:** ADR-0003, threat model I-07, `SigningKeyRotationService`, `SigningKeyRotationTests`

Signing keys sign every access and ID token. Refresh tokens are protected by a separate HMAC key.

| Situation | Procedure | User impact |
| --------- | --------- | ----------- |
| Scheduled rotation (recommended every 90 days) | [Planned rotation](#1-planned-rotation) | None |
| Suspected or confirmed key compromise | [Emergency rotation](#2-emergency-rotation) | Every user in the realm must sign in again |

All commands use the developer CLI. Point it at the target environment with `DOVEPEAK_Keycloak__BaseUrl`, `DOVEPEAK_Keycloak__ClientId` and `DOVEPEAK_Keycloak__ClientSecret`. **Never pass secrets on the command line in shared terminals**; export them from your secret store.

```bash
alias dovepeak-dev='dotnet run --project tools/Dovepeak.Identity.DevTool --'
```

---

## 1. Planned Rotation

### 1.1 Rotate

```bash
dovepeak-dev rotate-keys --realm <realm>
```

* A new RS256 key becomes **active** and signs all new tokens.
* Previous keys become **passive**: still published in JWKS, so tokens they signed keep validating.

### 1.2 Verify

* New sign-ins produce tokens whose `kid` header is the new key.
* `GET <issuer>/protocol/openid-connect/certs` lists both the new and the old key.
* No increase in `401` responses from resource servers.

### 1.3 Retire the old key (after the overlap period)

Wait **at least the longest access token lifetime plus the longest resource-server JWKS cache lifetime** (default: 10 + 10 minutes; use 1 hour for margin), then:

```bash
dovepeak-dev retire-keys --realm <realm> --min-age 01:00:00
```

The command only deletes keys that have been passive for at least `--min-age`, so running it too early is safe: it does nothing.

---

## 2. Emergency Rotation

Use when a private signing key or the realm's HMAC key may have been exposed.

### 2.1 Decide

Emergency rotation **signs out every user in the realm**. The incident lead approves it. If several realms may be affected, rotate each one.

### 2.2 Rotate

```bash
dovepeak-dev emergency-rotate-keys --realm <realm> --confirm
```

This, in order:

1. Creates new RS256 signing and HS512 HMAC keys.
2. Deletes **all** previous RSA and HMAC keys (no overlap: tokens signed by them must stop working).
3. Revokes every session in the realm and rejects every token issued before now.

### 2.3 Force resource servers to drop the old key

Resource servers that cached the JWKS keep accepting old access tokens until their cache refreshes. Restart, or trigger a JWKS refresh in, every resource server for the affected tenant. Dovepeak SDKs bound the cache lifetime to limit this window (ADR-0003).

### 2.4 Verify

* `GET <issuer>/protocol/openid-connect/certs` contains only the new key.
* An old refresh token fails with `invalid_grant`.
* An old access token is rejected by a freshly started resource server.
* Users can sign in again.

### 2.5 Follow up

* Record the incident, affected realms and timeline in the audit log and incident tracker.
* Identify how the key was exposed before closing the incident.
* Notify affected tenants according to the disclosure policy (`SECURITY.md`).

---

## Rehearsal

This runbook is exercised automatically by `SigningKeyRotationTests` on every CI run, and must be rehearsed manually once per quarter in staging by an engineer who did not write it (milestone M6.3).
