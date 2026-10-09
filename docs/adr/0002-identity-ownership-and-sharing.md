# ADR-0002: Identity Ownership and Sharing Model

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (sections 7 and 17) requires the identity-sharing model to be explicitly defined, because it determines account isolation and single sign-on behaviour.

The tenancy hierarchy is: Platform → Organization → Project → Environment → Application.

## Options Considered

1. **Identity per organization** — one user directory shared by all projects in an organization. Simple SSO across projects, but unrelated projects (for example two different client products built by one agency) would share end users.
2. **Identity per project environment** — each environment of each project has its own user directory. Applications within that environment can share it.
3. **Identity per application** — each application has its own directory. Strongest isolation, but no SSO between related applications.

## Decision

**Option 2.** An end-user identity belongs to exactly one **project environment**.

* A user registered in `shop-prod` does not exist in `shop-staging`, `shop-dev` or any other project.
* Applications in the same project environment share its user directory and can provide single sign-on. Sharing is opt-in per application; an application can be restricted to its own assigned users.
* Development, staging and production data are always isolated from each other.
* Cross-project and cross-organization identity federation is out of MVP scope and will require its own ADR.

Developer accounts (people who use the Dovepeak portal) are separate from end-user identities and live in a dedicated platform directory.

## Consequences

* Maps directly onto Keycloak realms (ADR-0001): one realm per project environment.
* Creating a project provisions one realm per environment.
* An organization that wants SSO across multiple products places them in one project.
* A future "shared directory" feature would require federation between realms.

## Validation

* M3.4: creating a project provisions isolated realms per environment.
* M3.9: the tenant-isolation suite proves that no identity, session or credential is visible across project environments.
