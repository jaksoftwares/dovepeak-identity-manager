# Dovepeak Identity — Threat Model

**Version:** 0.4 (Phase 6 review)
**Method:** STRIDE per trust boundary
**Status:** Draft — to be reviewed at every phase gate and before every major release

This model covers the MVP architecture defined in [ADR-0001](../adr/0001-identity-engine-selection.md). It will be updated as components are built.

---

## 1. System Overview and Trust Boundaries

```text
 ┌──────────────── Internet (untrusted) ────────────────────────────────────┐
 │                                                                          │
 │  End users ──▶ Client applications (SPA / mobile / BFF / backend APIs)   │
 │                         │                                                │
 │  Developers ──▶ Developer portal (browser)                               │
 │                         │                                                │
 └─────────────────────────┼────────────────────────────────────────────────┘
              TB1 ═════════╪═════════  Edge: Caddy / Traefik (TLS, rate limits)
 ┌─────────────────────────┼──────── Dovepeak platform network ─────────────┐
 │                         ▼                                                │
 │   Keycloak public endpoints  ◀── OIDC ──  Management API  ◀── Portal BFF │
 │   (login, token, JWKS)                        │                          │
 │              │                     TB2 ═══════╪═══════ Admin boundary    │
 │              │                                ▼                          │
 │              │                     Keycloak Admin API (private only)     │
 │   TB3 ═══════╪════════════════════════════════╪════════ Data boundary    │
 │              ▼                                ▼                          │
 │       PostgreSQL (keycloak db)      PostgreSQL (dovepeak db)   Valkey    │
 │                                                                          │
 │   TB4 ═══ Outbound: mail relay → SMTP provider, webhook destinations ═══ │
 │   TB6 ═══ Telemetry: collector, Prometheus, Tempo, Grafana (operators) ═══│
 └──────────────────────────────────────────────────────────────────────────┘

 TB5: Tenant boundary — logical, crosses every component above.
```

| ID  | Boundary                    | Description                                                                 |
| --- | --------------------------- | --------------------------------------------------------------------------- |
| TB1 | Internet → platform         | All public traffic. Untrusted input, unauthenticated callers, bots.         |
| TB2 | Management API → engine admin | Highly privileged administrative access to Keycloak.                      |
| TB3 | Services → data stores      | Persistent identity, configuration and audit data.                          |
| TB4 | Platform → external services | Email delivery and webhook calls to tenant-controlled URLs.                |
| TB5 | Tenant ↔ tenant             | Logical isolation between organizations, projects and environments.         |
| TB6 | Platform → telemetry and operators | Traces, metrics and logs leave the services; operator UIs (Grafana, Prometheus, Alertmanager, Keycloak admin) are reachable only from the host. |

## 2. Assets

| Asset                                   | Sensitivity | Location                         |
| --------------------------------------- | ----------- | -------------------------------- |
| End-user password verifiers             | Critical    | Keycloak database                |
| Token signing private keys              | Critical    | Keycloak database / key provider |
| Refresh tokens and sessions             | High        | Keycloak                         |
| Confidential client secrets             | High        | Keycloak                         |
| Developer API key digests and HMAC key  | High        | Dovepeak database / deployment secrets |
| Keycloak admin credentials              | Critical    | Deployment secrets               |
| Tenant configuration                    | Medium      | Dovepeak database                |
| End-user personal data (email, profile) | High        | Keycloak database                |
| Audit logs                              | Medium      | Dovepeak database                |
| Database backups and WAL archive        | Critical    | pgBackRest repository (local volume or S3), encrypted |
| Backup encryption passphrase            | Critical    | Deployment secrets (kept off the server as well) |
| Traces, metrics and logs                | Medium      | Tempo, Prometheus, container logs |

## 3. Threats by STRIDE Category

Status: **Verified** = mitigation implemented and covered by automated tests; **Partial** = some mitigations verified; **Planned** = scheduled in a milestone; **Open** = needs design.

### Spoofing

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| S-01 | Credential stuffing and brute force against login              | TB1      | Keycloak brute-force detection (5 failures), edge per-IP rate limiting backed by Valkey | M2.1, M2.4  | Verified (`BruteForceProtectionTests`, `EdgeProtectionTests`) |
| S-02 | Forged JWT accepted by a resource server (`alg: none`, key confusion) | TB1 | Asymmetric signing, algorithm allow-list, issuer and audience validation; SDKs refetch keys on rotation | M1.3, M5.3, M5.4 | Verified (.NET SDK through the example API: `TokenValidationTests`, `DotnetSdkTests`, `SigningKeyRotationTests`; Node SDK: `node.test.ts`) |
| S-03 | Authorization code interception                                | TB1      | PKCE enforced realm-wide by client policy; exact redirect URI matching; codes single-use | M2.1        | Verified (`ClientPolicyTests`, `AuthorizationCodeFlowTests`) |
| S-04 | Stolen developer API key used against the Management API       | TB1      | Prefixed keys for leak detection, scopes, expiry, immediate revocation                  | M3.6        | Verified (`CredentialApiTests`: shown once, keyed HMAC digest, scope subset of creator, human-only scopes, expiry, immediate revocation) |
| S-05 | Phishing via open redirect on login or logout                  | TB1      | Exact-match redirect and post-logout URIs; wildcards rejected at registration          | M2.1, M3.5  | Verified (`ClientPolicyTests`, `RealmProvisioningTests`) |
| S-06 | Mail relay used as an open relay, or one tenant sending as another | TB4  | Relay reachable only on the internal network; every session must authenticate with a realm name and the relay password; unauthenticated senders rejected | M6.4 | Verified (`RelayTests`). Residual: realms share one relay password, so someone with Keycloak admin access could attribute mail to another realm's quota |
| S-07 | Client address spoofed with `X-Forwarded-For` to evade rate limits and brute-force detection | TB1 | Production: Caddy replaces untrusted forwarded headers; the edge trusts only Caddy's fixed address, Keycloak only the edge's (L-09) | M6.5 | Partial (verified manually in the production rehearsal, 2026-10-10: limits keyed per client; no automated test) |

### Tampering

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| T-01 | Direct modification of Keycloak configuration bypassing Dovepeak | TB2    | Admin API blocked on the public edge (ADR-0007); reconciliation worker reverts drift   | M2.4, M3.7  | Verified (edge: `EdgeProtectionTests`; realm settings, client policies (PKCE, grant restrictions, secret rotation), scopes, client configuration and token lifetimes reverted and audited: `ReconciliationTests`, `ScopeApiTests`) |
| T-02 | CSRF against portal or BFF session cookies                     | TB1      | SameSite cookies, Origin checks on state-changing BFF routes                            | M1.2, M4.1  | Verified (BFF: `tests/e2e/bff-smoke.mjs`; portal: `portal/e2e` — SameSite/HttpOnly cookies, cross-site sign-out rejected, Referrer-Policy keeps the Origin check effective) |
| T-03 | Webhook payload forgery received by tenant systems             | TB4      | HMAC-signed webhooks with timestamp to prevent replay                                   | M3.9        | Verified (`WebhookSignerTests`, `AuditAndWebhookApiTests`: `Dovepeak-Signature: t=…,v1=…`, 5-minute replay window) |
| T-04 | Tenant branding abused for phishing, markup injection or email header injection | TB1, TB4 | HTTPS-only logo URLs, escaped text, single-line subjects, platform-controlled links, buttons and footer; values re-checked in templates; drift reverted | M4.3 | Verified (`TenantBrandingTests`, `BrandingApiTests`) |

### Repudiation

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| R-01 | Administrator denies making a security-relevant change         | TB2      | Append-only administrative audit log with actor, tenant, time and change                | M3.9        | Verified (`AuditAndWebhookApiTests`: actor recorded; UPDATE/DELETE rejected by a database trigger) |
| R-02 | Authentication events lost or not attributable                 | TB3      | Explicit security event types stored by Keycloak; idempotent collector into tenant-scoped audit store | M2.5        | Verified (`AuditCollectionTests`) |

### Information Disclosure

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| I-01 | **Cross-tenant data access through the Management API**        | TB5      | Central authorization layer, EF Core tenant filters, PostgreSQL RLS, isolation test suite in CI | M3.2, M3.9 | Verified (`TenantIsolationTests` probes every organization-scoped endpoint as owner and as API key of another organization; RLS with FORCE in the `MultiTenancy` migration) |
| I-02 | Account enumeration via registration or recovery responses     | TB1      | Uniform responses for existing and non-existing accounts (login, recovery)              | M2.4        | Verified (`AuthorizationCodeFlowTests`, `PasswordRecoveryTests`) |
| I-03 | Secrets in logs, errors or health responses                    | TB1, TB3 | Allow-listed audit details, request paths dropped from edge logs, CI log scanner, status-only health responses | M0.3, M2.5 | Verified (`scripts/ci/scan-logs-for-secrets.sh`). Finding: edge logs contained action tokens until ADR-0007 decision 4 |
| I-04 | Secrets shipped in frontend bundles                            | TB1      | Separate browser and server SDK configuration types; the browser client refuses a secret; public config endpoint tested | M3.5, M5.1  | Verified (`ApplicationApiTests`, SDK `client.test.ts`) |
| I-05 | Refresh tokens stolen from browser storage                     | TB1      | BFF pattern: tokens held server-side, opaque HttpOnly session cookie; SPA tokens in memory only | M1.2, M5.2, M5.5  | Verified (`bff-smoke.mjs` on the SDK's Next.js adapter; the React SPA browser test checks nothing is left in storage; `docs/sdk/browser-threat-model.md`) |
| I-06 | Database backup exposure                                       | TB3      | pgBackRest encrypts backups and WAL (AES-256) with a passphrase kept outside the repository; the repository is never served (backup metrics come from a separate directory); monthly restore drills | M6.2        | Verified (`scripts/ops/restore-drill.sh` restores from the encrypted repository). Operators must keep an off-site copy (S3 repository) |
| I-07 | Signing key compromise                                         | TB3      | Planned and emergency rotation (one realm or every realm), runbooks, keys never in source control | M2.3, M6.3  | Verified (`SigningKeyRotationTests`; runbooks `signing-key-rotation.md`, `signing-key-compromise.md`; `emergency-rotate-keys --all`) |
| I-08 | Secrets or personal data leaking into telemetry               | TB6      | No query strings recorded at source; the collector strips query strings, `Authorization` and `Cookie` again; relay logs hash email addresses; probes untraced | M6.1 | Partial (manual scan of 40 sign-in traces found no codes or tokens, 2026-10-10; the CI log scanner covers logs, not traces) |
| I-09 | Operator interfaces exposed (Grafana, Prometheus, Alertmanager, Keycloak admin console) | TB6 | Bound to 127.0.0.1 on the host and reached through an SSH tunnel; Keycloak admin hostname separate from the public one | M6.5 | Verified (production rehearsal: `/admin`, `/realms/master`, `/metrics`, `/health` return 403 on the public hostname) |

### Denial of Service

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| D-01 | Authentication outage blocks every dependent application       | TB1      | Two-node Keycloak cluster behind the edge; SLOs with burn-rate alerts; readiness probes; runbooks; point-in-time recovery | M6.1–M6.3   | Partial (node failover verified by `tests/e2e/ha-failover.sh`; restore drill within RPO and RTO). Residual: the reference deployment runs on one host, which remains a single point of failure — host loss means a restore on a new host (RTO 1 hour) |
| D-02 | Email or SMS cost abuse through repeated recovery requests     | TB4      | Per-IP limit on email-sending endpoints; mail relay quotas per recipient (5/hour, 20/day) and per tenant (500/hour), excess dropped silently | M2.2, M2.4, M6.4  | Verified (`EdgeProtectionTests`, `PasswordRecoveryTests.RecoveryEmails_ToOneAccount_AreCappedByTheMailRelay`, `RelayTests`) |
| D-03 | One tenant exhausting shared resources ("noisy neighbour")     | TB5      | Per-tenant quotas and rate limits; tenant sharding across Keycloak clusters             | M3.9, M1.6, M6.4  | Partial (per-organization resource quotas and a per-tenant email quota; per-tenant request rate limits and sharding pending) |
| D-05 | Load beyond capacity degrades sign-in for everyone             | TB1      | Edge rate limits; capacity measured with k6; latency SLO alerts                         | M6.1, M6.4  | Partial (baseline measured on the rehearsal host: `docs/operations/load-testing.md`; production capacity must be measured on production hardware) |
| D-04 | Realm count growth degrading Keycloak performance              | TB5      | Measured in M1.6 (Phase 1 validation report); constant-size admin token; `tenant → cluster` mapping for sharding | M1.6, M3.4  | Partial (`tenant → cluster` stored per environment; only the `default` cluster is routed — L-12) |

### Elevation of Privilege

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| E-01 | Compromised Management API gains full Keycloak admin           | TB2      | Service account holds only master `create-realm`; admin rights only over realms it created; private network only | M1.1        | Verified (`RealmProvisioningTests`; E-01 answered) |
| E-02 | Organization member exceeds their role                         | TB5      | Role checks in central authorization layer; tests per role                              | M3.3        | Verified (`PermissionMatrixTests`, `OrganizationApiTests` role checks; API keys cannot exceed their creator) |
| E-03 | Server-side request forgery through webhook URLs               | TB4      | Block private and link-local address ranges; resolve-then-connect checks                | M3.9        | Verified (`WebhookUrlPolicyTests`, `AuditAndWebhookApiTests`: HTTPS only, no credentials, private/link-local/CGNAT/multicast blocked, address re-checked at connect time) |
| E-04 | Vulnerable dependency or container image                       | All      | CodeQL, Trivy, Dependabot, pinned image versions                                        | M0.2        | Implemented |

## 4. Open Questions

1. ~~**E-01:** Can the Management API's Keycloak account be restricted?~~ **Answered (M1.1):** yes. With only the master `create-realm` role, the account administers the realms it created and receives 403 for every other realm, including master.
2. ~~Where are signing keys stored at rest?~~ **Decided (Phase 6):** in Keycloak's database (generated key providers), protected by database access controls, host disk encryption and encrypted backups. An external HSM/KMS key provider is deferred until a customer requires it; compromise is handled by `signing-key-compromise.md`.
3. ~~Encryption at rest without a cloud KMS?~~ **Decided (Phase 6):** full-disk encryption on the host (LUKS, in the self-hosting guide) plus pgBackRest encryption of every backup and WAL segment. The backup passphrase is stored outside the server.

## 5. Review Log

| Date       | Version | Change                         |
| ---------- | ------- | ------------------------------ |
| 2026-10-09 | 0.1     | Initial threat model (M0.4)    |
| 2026-10-09 | 0.2     | Phase 1–2 verification; E-01 answered; I-03 log finding |
| 2026-10-10 | 0.4     | Phase 5–6 review: SDK verification (S-02, I-04, I-05); mail relay (S-06, D-02); production network trust (S-07, I-09); telemetry (TB6, I-08); backups (I-06); clustering and capacity (D-01, D-05); open questions 2 and 3 decided |
