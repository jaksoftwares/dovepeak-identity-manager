# Runbook: Email Provider Outage

**Applies to:** every environment that sends email
**Owner:** Platform operations
**Related:** alert `EmailDeliveryFailing`, email SLO ([service-level-objectives.md](../operations/service-level-objectives.md)), mail relay (`services/mail-relay`)

All identity email — address verification, password reset, security alerts — leaves through the Dovepeak mail relay, which forwards to one SMTP provider. When the provider fails, the relay answers Keycloak with a temporary error. **Nothing is queued**: users must request the email again once delivery works.

**Users are not told.** On password recovery Keycloak always shows "You should receive an email shortly" — revealing a failure would tell an attacker which accounts exist. Users simply never receive the email, so `EmailDeliveryFailing` is the only signal. Failed emails do not count against the per-recipient quota, so users can retry as soon as delivery works.

## Impact

| Flow | During the outage |
| ---- | ----------------- |
| Sign-in with an existing password | Works |
| Registration | Account is created, but the verification email does not arrive; the user cannot finish |
| Password recovery | No reset email; users who forgot their password are locked out |
| Security alerts (password changed, lockout) | Not delivered |

## 1. Confirm

1. Alert `EmailDeliveryFailing`, or the dashboard's *Emails by outcome* panel shows `failed`.
2. Find the reason:
   ```bash
   docker compose logs --tail 50 mail-relay | grep -i "forwarding email"
   ```
   The exception names the cause: connection refused or timeout (provider down or blocked), `535` authentication failure (credentials revoked or rotated), `550`/`554` (sending suspended, often for reputation).
3. Check the provider's status page.

## 2. Mitigate

**Credentials rotated or expired** — update `SMTP_USERNAME` / `SMTP_PASSWORD` in `.env`, then:
```bash
docker compose up -d --wait mail-relay
```

**Provider down or account suspended** — switch to the secondary provider. Only the relay knows the provider, so the switch needs no Keycloak or realm change:

1. Set `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD` to the secondary provider.
2. The sender domain (`MAIL_FROM`) must already be verified with the secondary provider, with its SPF include and DKIM key published. Set this up **before** you need it; DNS changes during an incident take too long.
3. Apply: `docker compose up -d --wait mail-relay`.

**No secondary provider** — there is no workaround that keeps recovery safe (the recovery email is the proof of mailbox ownership). Communicate instead (step 4).

## 3. Verify

* Trigger a password reset for a test account in a test realm; the email arrives (`tests/e2e/email-outage-drill.mjs`, see Rehearsal).
* *Emails by outcome* shows `sent` again; `EmailDeliveryFailing` resolves within 10 minutes.

## 4. Communicate

* Post on the status page: "Verification and password-reset emails are delayed. Existing users can sign in normally."
* After recovery, tell tenants that users who tried to register or reset a password during the window must request a new email (links were never sent).

## 5. Afterwards

* Record start, end and cause; the outage counts against the email SLO.
* If the provider suspended the account, find out why (bounce rate, complaints) before switching back.
* Confirm quotas were not the cause: dropped-by-quota emails show as `throttled`, not `failed`, and do not trigger this alert.

## Rehearsal

In staging, simulate the outage without editing `.env`, then recover:

```bash
SMTP_HOST=smtp.unreachable.invalid docker compose up -d --no-deps --wait mail-relay   # outage
# request resets for an existing account (tests/e2e/email-outage-drill.mjs, run on the frontend network)
docker compose up -d --no-deps --wait mail-relay                                       # recovery
```

| Date | Result |
| ---- | ------ |
| 2026-10-10 | Production rehearsal. Three resets during the outage: users saw the normal confirmation page, the relay logged the failures, `EmailDeliveryFailing` (critical) reached Alertmanager about 3 minutes after the outage began. After recovery the next reset email was delivered. The first run found that failed emails used up the recipient's quota, so recovery emails were dropped after the outage; fixed (failed emails are refunded) before the final run. |

This runbook must still be rehearsed once by an engineer who did not write it (milestone M6.3).
