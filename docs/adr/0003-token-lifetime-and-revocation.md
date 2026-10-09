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

## Validation

* M1.4: rotation and reuse detection demonstrated.
* M2.6: automated tests for expiry, revocation and reuse.
* M5.3 / M5.4: SDK introspection mode tested.
