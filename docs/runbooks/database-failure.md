# Runbook: Database Failure and Restoration

**Applies to:** the production deployment (`deploy/production`)
**Owner:** Platform operations
**Objectives:** RPO 15 minutes, RTO 1 hour ([service-level-objectives.md](../operations/service-level-objectives.md))
**Related:** milestone M6.2, `scripts/ops/restore-drill.sh`, alerts `ServiceDown`, `WalArchivingFailing`, `BackupStale`

One PostgreSQL server holds two databases: `dovepeak` (organizations, projects, applications, audit) and `keycloak` (realms, users, credentials, sessions). Losing it stops sign-in for every tenant. pgBackRest keeps encrypted full and differential backups plus every WAL segment, so the server can be restored to any second since the oldest retained full backup.

| Situation | Go to |
| --------- | ----- |
| PostgreSQL stopped or keeps restarting, data directory intact | [1. Restart](#1-restart-the-database) |
| Data damaged or deleted by mistake (bad migration, `DELETE` without `WHERE`, corruption) | [2. Point-in-time restore in place](#2-point-in-time-restore-in-place) |
| Host or disk lost | [3. Restore on a new host](#3-restore-on-a-new-host) |
| WAL archiving failing (`WalArchivingFailing`) | [4. Archiving failures](#4-archiving-failures) |

All commands run in `deploy/production` on the server.

---

## 0. First five minutes

1. **Declare an incident** and name an incident lead. Note the time: it is the start of the RTO clock.
2. **Look before acting**:
   ```bash
   docker compose ps
   docker compose logs --tail 100 postgres
   df -h /var/lib/docker
   ```
3. **Do not delete or "clean up" anything** in the `postgres-data` or `backup-repo` volumes. Every restore below works from them.
4. If the cause is a destructive change (a bad deploy, a mistaken command), **write down the exact time it happened** (UTC). That is the restore target.

---

## 1. Restart the database

Use when the data directory is intact (logs show a crash, out-of-memory kill or full disk, not corruption).

1. If the disk was full, free space first (old Docker images: `docker image prune`; never the volumes above). A full disk is also the usual cause of `WalArchivingFailing`.
2. Restart and wait for health:
   ```bash
   docker compose up -d --wait postgres
   ```
3. PostgreSQL replays its own WAL on start; no data committed before the crash is lost.
4. Dependent services reconnect by themselves (bounded retries and connection pools). If a service stays unhealthy for more than two minutes, restart it: `docker compose up -d --wait management-api workers keycloak-1 keycloak-2`.
5. Continue with [Verify](#5-verify).

---

## 2. Point-in-time restore in place

Restores both databases to a moment before the damage. **Everything written after the target is lost**, including sign-ups and password changes; choose the latest safe moment.

1. **Pick the target time** (UTC), a few seconds before the damaging change. Check the backups cover it:
   ```bash
   docker compose exec pg-backup pgbackrest info
   ```
   The oldest full backup's start time must be earlier than the target.

2. **Stop everything that writes** (the database stays up for now):
   ```bash
   docker compose stop caddy portal management-api workers keycloak-2 keycloak-1 mail-relay pg-backup
   ```

3. **Take a safety copy of the damaged state** (lets you change your mind about the target):
   ```bash
   docker compose run --rm --no-deps --entrypoint pgbackrest pg-backup --type=full backup
   ```

4. **Stop PostgreSQL and restore** into its own data directory. `--delta` reuses unchanged files, which makes the restore much faster:
   ```bash
   docker compose stop postgres
   docker compose run --rm --no-deps --user postgres --entrypoint pgbackrest postgres \
     --delta --type=time --target="2026-10-10 14:32:00+00" --target-action=promote restore
   ```

5. **Start PostgreSQL**. It replays WAL up to the target, then promotes and opens for writes on a new timeline:
   ```bash
   docker compose up -d --wait postgres
   docker compose logs postgres | grep -E "recovery stopping|archive recovery complete"
   ```

6. **Start the rest**, Keycloak first:
   ```bash
   docker compose up -d --wait
   ```

7. **Take a new full backup** right away. Backups taken before the restore belong to the old timeline:
   ```bash
   docker compose exec pg-backup pgbackrest --type=full backup
   ```

8. Continue with [Verify](#5-verify).

---

## 3. Restore on a new host

Use when the server or its disk is gone. This needs an **off-site repository** (`BACKUP_REPO_TYPE=s3`): a repository on the lost disk is lost with it.

1. Provision a host following the [self-hosting guide](../self-hosting.md) up to and including "Configure", using the **same `.env`** (from your password manager or vault). `BACKUP_ENCRYPTION_PASSPHRASE` must be the original one or the backups cannot be decrypted.
2. Build the images and create the volumes without starting the stack:
   ```bash
   docker compose build
   docker compose create
   ```
3. Restore the newest state (omit `--type`/`--target` to replay every archived WAL segment):
   ```bash
   docker compose run --rm --no-deps --user root --entrypoint sh postgres -c \
     'chown postgres:postgres /var/lib/postgresql/data && gosu postgres pgbackrest restore'
   ```
4. Start the stack: `docker compose up -d --wait`.
5. Point DNS at the new host (or move the floating IP). Caddy obtains certificates on first request.
6. Take a new full backup (step 2.7), then continue with [Verify](#5-verify).

**Data lost:** at most the WAL not yet archived when the host died. Archiving runs at least every 60 seconds while there are writes (`archive_timeout`), well inside the 15-minute RPO.

---

## 4. Archiving failures

`WalArchivingFailing` means PostgreSQL cannot push WAL to the repository. Nothing is lost yet, but point-in-time recovery stops advancing and `pg_wal` grows until the disk fills.

```bash
docker compose logs --tail 50 postgres | grep -A2 "archive command failed"
```

| Message | Cause | Fix |
| ------- | ----- | --- |
| `No space left on device` | Repository disk full | Free space or lower `BACKUP_RETENTION_FULL`, then `docker compose exec pg-backup pgbackrest expire` |
| `403`, `AccessDenied`, `SignatureDoesNotMatch` | S3 credentials or bucket policy | Fix `BACKUP_S3_*` in `.env`, then `docker compose up -d postgres pg-backup` |
| `unable to find a valid repository` / `stanza ... does not exist` | Repository replaced or emptied | `docker compose exec pg-backup pgbackrest stanza-create`, then a full backup |
| `environment variable ... must have a value` | A `PGBACKREST_*` variable set but empty, bypassing the wrapper | Check `PGBACKREST_CMD` is set in `compose.yaml` |

PostgreSQL retries automatically; the alert clears once archiving succeeds. Confirm with `docker compose exec pg-backup pgbackrest check`.

---

## 5. Verify

* `docker compose ps`: every service healthy.
* Sign in to the portal and open an organization.
* A tenant's discovery document and JWKS answer: `curl -s https://<identity host>/realms/<realm>/.well-known/openid-configuration`.
* Grafana: availability recovering, `ServiceDown` cleared, no `ConfigurationDriftCorrected` storm (a storm after a restore means Keycloak and the platform database were restored to different moments; they share one server, so this indicates a mistake).
* The audit log shows activity up to the restore target and nothing after it.

## 6. Afterwards

* Record the incident: cause, target time, data lost (from the target to the failure), RTO achieved.
* If users' changes were lost (sign-ups, password resets), tell affected tenants so they can ask users to repeat them.
* Resource servers keep validating tokens signed by keys that existed at the target time; keys created after it are gone, so tokens they signed fail. This is expected and resolves as users sign in again.

## Rehearsal

`scripts/ops/restore-drill.sh` runs steps 2.4 and 2.5 against a scratch copy every month (results in [restore-drills.md](../operations/restore-drills.md)).

Section 2 was rehearsed end to end on the production rehearsal stack on 2026-10-10: a realm was deleted and a database dropped by "accident"; following steps 2.1–2.6 restored both (recovery stopped right before the destructive transaction) in 3 minutes 14 seconds, plus the time to restart Keycloak. That rehearsal corrected step 2.3 (the backup container needs `--entrypoint pgbackrest`).

This runbook as a whole must still be rehearsed once by an engineer who did not write it (milestone M6.3).
