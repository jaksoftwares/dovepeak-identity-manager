# Dovepeak Identity — Threat Model

**Version:** 0.1 (initial, milestone M0.4)
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
 │   TB4 ═══ Outbound: SMTP provider, webhook destinations ═══              │
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

## 3. Threats by STRIDE Category

Status: **Planned** = mitigation scheduled in a milestone; **Open** = needs design.

### Spoofing

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| S-01 | Credential stuffing and brute force against login              | TB1      | Keycloak brute-force detection, edge rate limiting, account and IP throttling           | M2.1, M2.4  | Planned |
| S-02 | Forged JWT accepted by a resource server (`alg: none`, key confusion) | TB1 | Asymmetric signing, algorithm allow-list in SDKs, issuer and audience validation | M1.3, M5.3  | Planned |
| S-03 | Authorization code interception                                | TB1      | PKCE required for all clients; strict redirect URI matching                             | M2.1        | Planned |
| S-04 | Stolen developer API key used against the Management API       | TB1      | Prefixed keys for leak detection, scopes, expiry, immediate revocation                  | M3.6        | Planned |
| S-05 | Phishing via open redirect on login or logout                  | TB1      | Exact-match redirect and post-logout URIs; no wildcards in production                   | M2.1, M3.5  | Planned |

### Tampering

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| T-01 | Direct modification of Keycloak configuration bypassing Dovepeak | TB2    | Admin API reachable only on private network; reconciliation worker reverts drift       | M3.7        | Planned |
| T-02 | CSRF against portal or BFF session cookies                     | TB1      | SameSite cookies, anti-forgery tokens, Origin checks                                    | M1.2, M4.1  | Planned |
| T-03 | Webhook payload forgery received by tenant systems             | TB4      | HMAC-signed webhooks with timestamp to prevent replay                                   | M3.9        | Planned |

### Repudiation

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| R-01 | Administrator denies making a security-relevant change         | TB2      | Append-only administrative audit log with actor, tenant, time and change                | M3.9        | Planned |
| R-02 | Authentication events lost or not attributable                 | TB3      | Keycloak event listener into tenant-scoped audit store                                  | M2.5        | Planned |

### Information Disclosure

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| I-01 | **Cross-tenant data access through the Management API**        | TB5      | Central authorization layer, EF Core tenant filters, PostgreSQL RLS, isolation test suite in CI | M3.2, M3.9 | Planned |
| I-02 | Account enumeration via registration or recovery responses     | TB1      | Uniform responses and timing for existing and non-existing accounts                     | M2.4        | Planned |
| I-03 | Secrets in logs, errors or health responses                    | TB1, TB3 | Redaction, problem-details errors without internals, health endpoints report status only | M0.3, M2.5 | Partially implemented (health endpoints) |
| I-04 | Secrets shipped in frontend bundles                            | TB1      | Separate browser and server SDK configuration types; public config endpoint tested      | M3.5, M5.1  | Planned |
| I-05 | Refresh tokens stolen from browser storage                     | TB1      | BFF pattern recommended; documented storage guidance for public clients               | M5.2        | Planned |
| I-06 | Database backup exposure                                       | TB3      | Encrypted backups, restricted access, restore drills                                    | M6.2        | Planned |
| I-07 | Signing key compromise                                         | TB3      | Key rotation, emergency rotation runbook, keys never in source control                 | M2.3, M6.3  | Planned |

### Denial of Service

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| D-01 | Authentication outage blocks every dependent application       | TB1      | Clustered Keycloak, SLOs, health probes, runbooks                                       | M6.1–M6.3   | Planned |
| D-02 | Email or SMS cost abuse through repeated recovery requests     | TB4      | Per-account and per-tenant sending quotas                                               | M2.2        | Planned |
| D-03 | One tenant exhausting shared resources ("noisy neighbour")     | TB5      | Per-tenant quotas and rate limits; tenant sharding across Keycloak clusters             | M3.9, M1.6  | Planned |
| D-04 | Realm count growth degrading Keycloak performance              | TB5      | Measured threshold; `tenant → cluster` mapping for sharding                             | M1.6, M3.4  | Planned |

### Elevation of Privilege

| ID   | Threat                                                         | Boundary | Mitigation                                                                              | Milestone   | Status  |
| ---- | -------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- | ----------- | ------- |
| E-01 | Compromised Management API gains full Keycloak admin           | TB2      | Per-realm service accounts with least privilege where possible; private network only    | M3.4        | Open    |
| E-02 | Organization member exceeds their role                         | TB5      | Role checks in central authorization layer; tests per role                              | M3.3        | Planned |
| E-03 | Server-side request forgery through webhook URLs               | TB4      | Block private and link-local address ranges; resolve-then-connect checks                | M3.9        | Planned |
| E-04 | Vulnerable dependency or container image                       | All      | CodeQL, Trivy, Dependabot, pinned image versions                                        | M0.2        | Implemented |

## 4. Open Questions

1. **E-01:** Can Keycloak's fine-grained admin permissions restrict the Management API's service account per realm, or must one platform-wide admin account be used? To be answered in M1.1.
2. Where are signing keys stored at rest — Keycloak's database or an external key provider (HSM / KMS)? To be decided before M2.3.
3. What is the encryption-at-rest approach for self-hosted deployments without a cloud KMS? To be decided before M6.2.

## 5. Review Log

| Date       | Version | Change                         |
| ---------- | ------- | ------------------------------ |
| 2026-10-09 | 0.1     | Initial threat model (M0.4)    |
