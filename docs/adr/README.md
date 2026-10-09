# Architecture Decision Records

This directory records significant architectural decisions for Dovepeak Identity.

## Process

1. Copy [template.md](template.md) to `NNNN-short-title.md` using the next number.
2. Open a pull request with status **Proposed**.
3. Once approved, set the status to **Accepted** and merge.
4. Accepted ADRs are never rewritten. To change a decision, write a new ADR and mark the old one **Superseded by ADR-NNNN**.

## Index

| ADR                                                    | Title                                     | Status                                   |
| ------------------------------------------------------ | ----------------------------------------- | ---------------------------------------- |
| [0001](0001-identity-engine-selection.md)              | Identity engine selection                 | Proposed — Phase 1 recommends GO        |
| [0002](0002-identity-ownership-and-sharing.md)         | Identity ownership and sharing model      | Proposed                                 |
| [0003](0003-token-lifetime-and-revocation.md)          | Token lifetime and revocation semantics   | Proposed                                 |
| [0004](0004-credential-types.md)                       | Credential types and handling             | Proposed                                 |
| [0005](0005-cache-engine-valkey.md)                    | Cache and transient state engine          | Proposed                                 |
| [0006](0006-hosted-login-theming.md)                   | Hosted login theming approach             | Proposed                                 |
| [0007](0007-edge-proxy-and-admin-separation.md)        | Edge proxy, rate limiting, admin separation | Proposed                               |
| [0008](0008-tenant-isolation-in-the-management-plane.md) | Tenant isolation in the management plane | Proposed                                 |
| [0009](0009-developer-portal-architecture.md)          | Developer portal architecture             | Proposed                                 |
