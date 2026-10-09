# Known Security Limitations

Dovepeak Identity documents its limitations openly (problem statement §16: "Security limitations and supported flows are documented honestly"). Each item states the impact, the current mitigation and the plan.

Last reviewed: 2026-10-09 (end of Phase 2).

| # | Limitation | Impact | Current mitigation | Plan |
| - | ---------- | ------ | ------------------ | ---- |
| L-01 | **Access token revocation is not instant** for resource servers that validate JWTs locally. | A revoked session's access token stays usable for up to its lifetime (default 10 minutes). | Short access token lifetime; optional introspection for sensitive APIs (ADR-0003). | SDK introspection mode (M5.3, M5.4). |
| L-02 | **JWKS caching delays emergency key rotation** at resource servers. | Old access tokens accepted until the resource server refreshes its key cache. | Runbook step 2.3 (restart or refresh resource servers). | SDKs bound the JWKS cache to 10 minutes (Phase 5). |
| L-03 | **Idle session timeout has a 2-minute grace window** (Keycloak behaviour). | Idle sessions end up to 2 minutes later than configured. | Absolute lifetime is exact. | Accepted. |
| L-04 | **No per-account email throttling.** Rate limits are per IP. | An attacker using many IPs could trigger many recovery emails to one address. | Per-IP edge limits; Keycloak action tokens expire after 15 minutes. | Evaluate a Keycloak extension or Management API–mediated recovery (Phase 3). |
| L-05 | **No breached-password check** (e.g. Have I Been Pwned). | Users can choose a long password that is known to be breached. | Minimum length 12, password history, not-username, not-email. | Evaluate a breached-password policy provider (Phase 6). |
| L-06 | **Refresh token reuse revokes the client session, not the SSO session.** | After detected theft, the user's browser SSO session survives. | The attacker holds refresh tokens, not the SSO cookie; the stolen token family is dead. | Accepted (ADR-0003). |
| L-07 | **Per-tenant visual branding is limited to the tenant name.** | No per-tenant logo or colours on hosted pages yet. | Display name is HTML-encoded; platform theme applied everywhere. | Phase 4 (ADR-0006). |
| L-08 | **Realm provisioning slows as tenant count grows** (see Phase 1 validation report). | Slower onboarding at high tenant counts; not on the sign-in path. | Constant-size admin token; provisioning is asynchronous. | Shard tenants across Keycloak clusters at the measured threshold (ADR-0001). |
| L-09 | **Local stack trusts all forwarded-header sources.** | In production, a client reaching Keycloak directly could spoof its IP. | Keycloak is only reachable through the edge; admin port is localhost-only. | Production deployment sets `KC_PROXY_TRUSTED_ADDRESSES` (M6.5). |
| L-10 | **Audit collection is near-real-time, not synchronous.** | Events appear in the Dovepeak audit store after up to one poll interval (default 15 s). Keycloak retains events for 30 days as a buffer. | Idempotent, checkpointed collection. | Consider an event-listener extension if lower latency is required. |
| L-11 | **No independent penetration test yet.** | Unknown vulnerabilities may exist. | Automated security tests, CodeQL, dependency and secret scanning. | Independent test before external customers (M6.4). |
