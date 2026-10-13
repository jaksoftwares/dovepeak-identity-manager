# Runbook: Tenant Sharding to a New Keycloak Cluster

**Applies to:** a deployment approaching the measured per-cluster realm limit
**Owner:** Platform operations
**Related:** ADR-0001, Phase 1 validation report, known limitations L-08 and L-12

> **Status: not executable yet.** Every environment records the cluster that hosts its realm (`environments.cluster`, always `default` today), but the Management API, the workers and the edge talk to a single Keycloak deployment (known limitation L-12). The procedure below is the agreed design; the code changes in [Prerequisites](#prerequisites) must ship first. Until then, act on the [early warning](#1-early-warning) by scaling the existing cluster vertically.

## Why

Keycloak slows down as realms accumulate: realm provisioning time and admin-token size grow with the realm count, and the Phase 1 validation report measured a practical threshold of about **300 realms per cluster**. Sign-in itself stays fast, so the symptom is slow onboarding first. Tenants are therefore spread across several independent Keycloak clusters, each with its own database.

## Design

* **Issuers do not change.** Every cluster is served under the same public hostname. The edge routes `/realms/<realm>/…` to the cluster that hosts the realm, so moving a tenant never changes its issuer URL, discovery document or redirect URIs, and integrations keep working.
* **New tenants go to the cluster with room** (`Platform:DefaultCluster`). Existing tenants move only when a cluster must be drained.
* **The platform database stays single.** Only realms are sharded; organizations, projects and applications remain in one PostgreSQL database, which is the source of truth reconciliation uses to rebuild a realm's configuration on any cluster.

## Prerequisites

Code changes required before this runbook can be used:

1. `Keycloak:Clusters` configuration: base URL and management credentials per cluster name.
2. A cluster-aware `KeycloakAdminClient` factory in the Management API and workers, selected by `environments.cluster`.
3. Edge routing per realm: a router per non-default cluster generated from the environment table (Traefik file provider, written by the workers), with `default` as the fallback.
4. Prometheus scraping and dashboards labelled by cluster.

## 1. Early warning

Alert well before the threshold, so there is time to add a cluster:

```promql
count(count by (realm) (keycloak_user_events_total)) > 240   # 80% of the measured limit
```

## 2. Add a cluster

1. Provision a second Keycloak cluster (two nodes) with its **own PostgreSQL database and backups**, using the production compose file as the template (`keycloak-1`, `keycloak-2`, `postgres`, `pg-backup` only).
2. Run the bootstrap job against it to create the management account.
3. Add it to `Keycloak:Clusters` and to the edge; verify with `tests/e2e/ha-failover.sh` adapted to the new nodes.
4. Set `Platform:DefaultCluster` to the new cluster. New environments land there from now on.

## 3. Move an existing tenant (drain)

Users' passwords, sessions and second factors live only in Keycloak, so moving a realm copies Keycloak data:

1. Announce a short maintenance window to the tenant: sign-in for that tenant is unavailable for minutes, and every user signs in again afterwards.
2. Export the realm with users from the old cluster (`kc.sh export --realm <realm> --users same_file`, run on a node that is stopped or in a dedicated export container against the same database).
3. Import it into the new cluster (`kc.sh import --file …`).
4. Set the environment's `cluster` to the new name and let reconciliation verify the configuration (`ConfigurationDriftCorrected` must stay silent).
5. Switch the edge route for the realm, then delete the realm from the old cluster.
6. Verify the tenant's discovery document and a sign-in.

## Rehearsal

Once the prerequisites ship, rehearse a move in staging with a realm of realistic size, then have an engineer who did not write this runbook perform it (milestone M6.3).
