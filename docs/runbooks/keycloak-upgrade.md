# Runbook: Keycloak Version Upgrade

**Applies to:** the production deployment (two Keycloak nodes)
**Owner:** Platform operations
**Related:** ADR-0001, known limitation L-13, `deploy/production/keycloak/Dockerfile`, `tests/e2e/ha-failover.sh`

Keycloak is pinned to an exact version everywhere (`compose.yaml`, `deploy/production/keycloak/Dockerfile`, CI). Upgrades are deliberate: security fixes within days of release, minor versions within a quarter.

## 1. Prepare (days before)

1. **Read the release notes and the upgrade guide** for every version between the current and the target. Look for: removed or renamed options, changes to client policies, the `client-secret-rotation` preview feature (L-13), user profile, themes (FreeMarker templates in `identity/keycloak/themes/dovepeak`), event types and admin API changes.
2. **Change the version in one branch** — every reference:
   ```bash
   git grep -n "keycloak:26\.\|keycloak/keycloak:" -- ':!*.md'
   ```
3. **Run the full CI suite against it.** The integration tests exercise the realm template, client policies, secret rotation, themes, drift reconciliation and the SDK contract tests; a breaking change fails them. Do not continue until CI is green.
4. **Check whether a rolling update is possible** (Keycloak decides from its configuration and caches). Always in a **throwaway container**, never with `docker compose exec` in a running node: `kc.sh` commands rebuild a node's configuration in place, and a node changed that way can fail on its next restart.
   ```bash
   mkdir -p /tmp/kc && chmod 777 /tmp/kc
   # Metadata from the version the nodes run now:
   docker run --rm -v /tmp/kc:/out dovepeak/keycloak:<current> update-compatibility metadata --file=/out/metadata.json
   # Check it with the new image:
   docker run --rm -v /tmp/kc:/out:ro dovepeak/keycloak:<new> update-compatibility check --file=/out/metadata.json
   ```
   Exit code **0**: rolling update ([2a](#2a-rolling-update-no-downtime)). Non-zero: full restart ([2b](#2b-full-restart-planned-downtime)).
5. **Schedule** a rolling update any time; schedule a full restart in a low-traffic window and announce it (it counts against the availability error budget).

## 2a. Rolling update (no downtime)

1. Fresh backup, so the database can be rolled back if the new version misbehaves:
   ```bash
   docker compose exec pg-backup pgbackrest --type=full backup
   ```
2. Build the new image (version bumped in `keycloak/Dockerfile` and the image tag in `compose.yaml`):
   ```bash
   docker compose build keycloak-1
   ```
3. Replace one node at a time. The edge takes the stopped node out of rotation within one health-check interval (5 s); sessions live in the database and the cluster, so users stay signed in.
   ```bash
   docker compose up -d --no-deps --wait keycloak-2
   docker compose logs keycloak-2 | grep "ISPN000094"       # rejoined the cluster with 2 members
   docker compose up -d --no-deps --wait keycloak-1
   ```
4. Continue with [Verify](#3-verify).

## 2b. Full restart (planned downtime)

Needed when the new version changes the cluster protocol or runs database migrations that the old version cannot run alongside.

1. Post the maintenance notice. Sign-in is unavailable for a few minutes.
2. Stop both nodes, then take a full backup (the database is now quiet):
   ```bash
   docker compose stop keycloak-1 keycloak-2
   docker compose exec pg-backup pgbackrest --type=full backup
   ```
3. Build and start. The first node runs the database migrations; wait for it before starting the second:
   ```bash
   docker compose build keycloak-1
   docker compose up -d --no-deps --wait keycloak-1
   docker compose up -d --no-deps --wait keycloak-2
   ```
4. Continue with [Verify](#3-verify).

## 3. Verify

* Both nodes healthy; the cluster view lists two members.
* `tests/e2e/ha-failover.sh` passes (sign-in through the public edge, then failover to one node).
* Grafana: sign-in latency and error rate back to their usual level; no `ConfigurationDriftCorrected` storm (would mean the new version changed defaults the realm template sets — reconciliation reverted them, but find out why).
* Spot-check a tenant's hosted sign-in page: branding and texts render correctly.

## 4. Roll back

Keycloak database migrations cannot be undone by starting the old version. To roll back:

1. Stop both nodes.
2. Restore the database to the backup taken in step 2a.1 or 2b.2 ([database-failure.md](database-failure.md#2-point-in-time-restore-in-place), target: the backup's stop time). Platform data written since is lost too, because both databases share the server; keep the window short.
3. Revert the version change and start the old image.

## Rehearsal

Rehearse every upgrade in staging first. This runbook must be rehearsed once by an engineer who did not write it (milestone M6.3).
