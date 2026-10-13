# Runbook: Signing Key Compromise

**Applies to:** one tenant realm, or the whole platform
**Owner:** Incident lead (security)
**Related:** [signing-key-rotation.md](signing-key-rotation.md), threat model I-07, ADR-0003, `SECURITY.md`

A stolen signing key lets an attacker mint access and ID tokens that every resource server of the tenant will accept, for any user, until the key is gone from the JWKS and from resource servers' caches. Treat suspected exposure as confirmed until proven otherwise.

## 1. Decide the scope

| What may have leaked | Scope | Go to |
| -------------------- | ----- | ----- |
| One realm's key (for example, an exported realm file shared by mistake) | One realm | [2. One realm](#2-one-realm) |
| The Keycloak database, a database backup with its encryption passphrase, a Keycloak node's disk or memory, or the Keycloak admin account | Every realm | [3. Whole platform](#3-whole-platform) |

Platform-wide rotation signs out **every user of every tenant**. The incident lead approves it.

## 2. One realm

Follow [signing-key-rotation.md, section 2](signing-key-rotation.md#2-emergency-rotation):

```bash
dovepeak-dev emergency-rotate-keys --realm <realm> --confirm
```

Then make the tenant's resource servers drop their cached JWKS (restart them, or call their JWKS refresh). Dovepeak SDKs refresh at most every 10 minutes on their own.

## 3. Whole platform

1. **Contain first.** If an attacker may still have access, cut it before rotating, or they take the new keys too:
   * Change the Keycloak admin password **in Keycloak first** (the bootstrap variable is only read on the very first start), then store the new value in `.env` so the bootstrap job can still sign in:
     ```bash
     docker compose exec keycloak-1 /opt/keycloak/bin/kcadm.sh set-password --server http://localhost:8080 \
       --realm master --user "$OLD_ADMIN" --password "$OLD_PASSWORD" --target-realm master --username "$OLD_ADMIN" --new-password "$NEW_PASSWORD"
     ```
   * Rotate the management client secret: set a new `KEYCLOAK_MANAGEMENT_CLIENT_SECRET` in `.env` and run `docker compose up -d --wait`. The bootstrap job writes the new secret into Keycloak and the platform services restart with it.
   * Rotate database passwords if the database itself was exposed.
   * Revoke the operator SSH keys and cloud credentials involved.
2. **Rotate every realm** (tenant realms and the platform realm; the management account cannot see `master`, which issues no tokens to tenants):
   ```bash
   dovepeak-dev emergency-rotate-keys --all --confirm
   ```
   The command rotates realm after realm, revokes all sessions in each, and continues past failures. If it reports failures, re-run it for the listed realms until none remain.
3. **Rotate the master realm keys** in the admin console (Realm settings → Keys → Providers: add a new RSA provider, delete the old ones). Master tokens are used only by operators and the bootstrap job.
4. **Resource servers**: notify every tenant (step 5) to restart or refresh JWKS in their APIs. Until they do, an access token minted with the old key is accepted for at most its lifetime plus the cache lifetime (10 + 10 minutes by default).
5. **Notify tenants** according to `SECURITY.md`: what happened, when, that users were signed out, and that they must refresh JWKS caches. Send it from the status page and email; do not rely on the portal's sign-in.

## 4. Verify

* For a sample of realms, `GET https://<identity host>/realms/<realm>/protocol/openid-connect/certs` lists only keys created after the rotation (compare `kid`s with the incident timeline).
* An access token captured before the rotation is rejected by a freshly started resource server (`401`).
* A refresh token issued before the rotation fails with `invalid_grant`.
* Users can sign in again; Grafana shows sign-ins recovering.

## 5. Afterwards

* Find how the key was exposed and close that path before closing the incident.
* Record the timeline, scope, and the number of realms rotated in the incident tracker; add `audit` notes.
* Review whether backups taken before the rotation must be destroyed: they contain the old keys. Backups are encrypted, so this matters only if the passphrase was exposed too.

## Rehearsal

Single-realm emergency rotation is exercised by `SigningKeyRotationTests` on every CI run. Rehearse `--all` in staging, then this runbook once with an engineer who did not write it (milestone M6.3).
