# ADR-0003: Token Lifetime and Revocation Semantics

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (sections 4.3, 4.4 and 17) requires short-lived access tokens, refresh token rotation with reuse detection, and a documented definition of how quickly revoked credentials stop being accepted. Locally validated JWTs remain usable until expiry unless additional controls are used.

## Decision

### Access tokens

* Format: JWT signed with an asymmetric algorithm (RS256 or ES256). Verification keys are published through JWKS.
* Default lifetime: **10 minutes**. Configurable per application between 5 and 60 minutes.
* Resource servers validate signature, issuer, audience, expiry and required claims locally.
* **Revocation latency:** a revoked session's access token may remain accepted by locally validating resource servers until it expires — at most the configured lifetime (10 minutes by default). This is documented publicly.

### Refresh tokens

* Rotated on every use. The previous refresh token is invalidated immediately.
* Reuse of an already-rotated refresh token is treated as theft: the entire token family and its session are revoked.
* Refresh tokens are bound to the client they were issued to.
* Public clients (SPA, native) use refresh tokens only through a BFF or with the platform's documented secure storage guidance.

### Immediate revocation

* Resource servers requiring immediate revocation call the token introspection endpoint instead of, or in addition to, local validation. The SDKs expose this as a configuration option.

### Sessions

* Idle timeout default: 30 minutes. Absolute timeout default: 12 hours. Both configurable per application.
* Password change, password reset, MFA change and administrative suspension revoke all sessions for the identity.

## Consequences

* Most APIs get fast, stateless validation with bounded revocation latency.
* High-sensitivity APIs pay the latency cost of introspection to gain immediate revocation.
* Default lifetimes are conservative and may be tuned after Phase 1 measurements.

## Measured Behaviour (Phase 1, Keycloak 26.4.0)

Verified by `RefreshTokenRotationTests`, `SessionLifetimeTests`, `PasswordRecoveryTests` and `SigningKeyRotationTests`:

| Scenario | Observed result |
| -------- | --------------- |
| Refresh | New refresh token issued; previous one invalid. |
| Reuse of a rotated refresh token | Rejected (`invalid_grant`). The **client session is revoked**: the legitimate holder's newest refresh token is rejected too. |
| Other clients of the same user | Unaffected by reuse in a different client. The user's SSO browser session also survives, so the user can sign in again without re-entering credentials if their browser session is still valid. |
| Back-channel logout | Refresh token rejected immediately. |
| Admin "log out user" | All sessions for the user, across clients, revoked. |
| Single session revocation | Only that session's tokens rejected; others remain active. |
| Absolute session lifetime | Refresh fails once the lifetime is reached, even for active sessions. |
| Password reset with "sign out other devices" | All previous sessions revoked. |
| Emergency key rotation | All refresh tokens rejected; access tokens rejected by any resource server that refreshes its JWKS. |

**Refinement of the decision:** "revokes the entire token family and its session" is implemented by Keycloak as revocation of the *client session*. This matches the threat (a stolen refresh token belongs to one client) and is accepted.

**Known limitation:** Keycloak applies a two-minute grace window to *idle* session timeouts. Idle expiry therefore happens up to two minutes later than configured. Absolute lifetimes are exact.

**Resource server guidance:** a resource server that caches JWKS keeps accepting tokens signed by a deleted key until its cache refreshes. SDKs must bound the JWKS cache lifetime (target: 10 minutes) so that emergency rotation takes effect within one access token lifetime.

## Validation

* M1.4: rotation and reuse detection demonstrated.
* M2.6: automated tests for expiry, revocation and reuse.
* M5.3 / M5.4: SDK introspection mode tested.
