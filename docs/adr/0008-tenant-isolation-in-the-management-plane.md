# ADR-0008: Tenant Isolation in the Management Plane

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The Management API stores every customer's organizations, projects, applications, API keys, webhooks and audit trail in one PostgreSQL database. Cross-tenant data access (threat I-01) is the most damaging failure a multi-tenant platform can have, and the most common cause is a single forgotten `WHERE organization_id = …`. The problem statement (§8.2) requires isolation to be enforced in depth, not by convention.

End-user identities are already isolated by the identity engine: one Keycloak realm per project environment (ADR-0002). This ADR covers the management data that Dovepeak itself owns.

## Decision

Isolation is enforced by four independent layers, any one of which blocks a cross-tenant read:

1. **Central authorization.** Every organization-scoped endpoint lives under `/v1/organizations/{orgId}` and carries an `OrganizationScoped` marker. `TenantAuthorizer` resolves the caller's membership (or API key) for that organization and checks the required permission. Roles nest strictly: viewer ⊂ developer ⊂ admin ⊂ owner. API keys hold explicit scopes that must be a subset of their creator's permissions, and can never hold the human-only scopes (`organization:manage`, `members:manage`, `api-keys:manage`).
2. **EF Core global query filters.** Every entity implementing `ITenantOwned` is filtered by the request's `TenantScope`. A query issued without entering a scope returns nothing.
3. **PostgreSQL Row-Level Security, forced.** Every tenant table has a `tenant_isolation` policy (`USING` and `WITH CHECK`) that matches `organization_id` against the session setting `dovepeak.org_id`, or allows all rows when `dovepeak.system` is `on` (workers only), with `FORCE ROW LEVEL SECURITY` so it also applies to the table owner the services connect as. `TenantSessionInterceptor` sets `dovepeak.org_id` (or `dovepeak.system` for workers) each time a connection is opened. With neither set, every tenant table reads as empty and rejects writes.
4. **Uniform 404.** A resource in another organization, or an organization the caller does not belong to, returns `404 Not Found`, never `403`, so existence is not disclosed.

The administrative audit log is **append-only**: a database trigger rejects `UPDATE` and `DELETE` on `management_audit_events`, whoever the caller is.

The **isolation suite** (`TenantIsolationTests`) discovers every endpoint from the running API's route table. It calls each one as the owner of organization A, and again with A's broadest API key, using the real IDs of organization B's resources, and requires 404 every time. It also fails if a `/v1` endpoint lacks the `OrganizationScoped` marker and is not on an explicit, reviewed list of unscoped endpoints (`/v1/me…`, `/v1/organizations`). New endpoints are covered without anyone writing new tests.

## Alternatives Considered

| Option | Why not chosen |
| ------ | -------------- |
| Database per tenant | Strongest isolation, but migrations, connection pooling and backups scale linearly with tenants; disproportionate for small management records. Realms already give end-user data per-tenant isolation. |
| Schema per tenant | Same operational cost as database-per-tenant with weaker guarantees. |
| Application-level filtering only | One missed filter or raw SQL statement leaks data; no defence in depth. |

## Consequences

- **Positive:** a bug in any single layer does not leak data; the suite proves the combined behaviour on every pull request.
- **Positive:** workers must opt in explicitly with `EnterSystem()`, which makes system-wide access visible in code review.
- **Negative:** session settings are applied when a connection opens. Code that changes `TenantScope` must do so before the `DbContext` opens its connection (the scope is set once per request or job, so this holds today).
- **Negative:** RLS policies are hand-written in migrations; a new tenant table must add one. The `MultiTenancy` migration is the reference, and the isolation suite catches a table that is missed through the API.
