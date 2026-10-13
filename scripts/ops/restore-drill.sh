#!/usr/bin/env bash
# Monthly restore drill (milestone M6.2): proves that backups and WAL archives restore to a point in time within the
# RPO (15 minutes) and RTO (1 hour) objectives, without touching the live database.
#
#   scripts/ops/restore-drill.sh            # run from anywhere; uses deploy/production/compose.yaml and its .env
#
# 1. Writes a "before" marker, notes the time, writes an "after" marker, and waits until the WAL is archived.
# 2. Restores the newest backup plus WAL to that time into a scratch volume, in a throwaway container.
# 3. Checks the restored copy: the "before" marker is present, the "after" marker is not, and the Dovepeak and Keycloak
#    databases hold the same organizations, environments and realms as the live database.
# 4. Prints a result row for docs/operations/restore-drills.md.
set -euo pipefail

cd "$(dirname "$0")/../../deploy/production"
export MSYS_NO_PATHCONV=1

dc() { docker compose "$@"; }
live_sql() { dc exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 -Atq "$@"; }
log() { echo "$(date -u +%H:%M:%S)  $*" >&2; }

id=$(date -u +%Y%m%d%H%M%S)
scratch="dovepeak-restore-drill-$id"
trap 'docker volume rm "$scratch" >/dev/null 2>&1 || true' EXIT

log "Preparing the drill database"
if [ -z "$(live_sql -d postgres -c "select 1 from pg_database where datname = 'restore_drill'")" ]; then
  live_sql -d postgres -c "create database restore_drill"
fi
live_sql -d restore_drill -c "create table if not exists marker (label text primary key, written_at timestamptz not null default clock_timestamp())"

log "Taking an incremental backup (the newest full or differential backup is its base)"
dc exec -T pg-backup pgbackrest --type=incr backup >/dev/null

live_sql -d restore_drill -c "insert into marker (label) values ('before-$id')"
target=$(live_sql -d restore_drill -c "select clock_timestamp()")
sleep 2
live_sql -d restore_drill -c "insert into marker (label) values ('after-$id')"
written=$(date +%s)

# Recovery refuses to run with fewer connections than the primary allows.
max_connections=$(live_sql -d postgres -c "show max_connections")

expected_orgs=$(live_sql -d dovepeak -c "select count(*) from organizations")
expected_envs=$(live_sql -d dovepeak -c "select count(*) from environments")
expected_realms=$(live_sql -d keycloak -c "select count(*) from realm")
expected_users=$(live_sql -d keycloak -c "select count(*) from user_entity")
expected_audit=$(live_sql -d dovepeak -c "select count(*) from audit_events")

log "Waiting for the WAL containing the markers to be archived"
dc exec -T pg-backup pgbackrest check >/dev/null
archive_lag=$(( $(date +%s) - written ))
log "Archived ${archive_lag}s after the write (archive_timeout bounds this at 60s under load)"

log "Restoring to $target into scratch volume $scratch"
started=$(date +%s)
result=$(dc run --rm --no-deps -T --user root -v "$scratch:/restore" \
  -e TARGET="$target" -e ID="$id" -e MAX_CONNECTIONS="$max_connections" --entrypoint sh pg-backup -s <<'RESTORE'
set -eu
mkdir -p /restore/data && chown postgres:postgres /restore/data && chmod 700 /restore/data
gosu postgres pgbackrest --pg1-path=/restore/data --type=time --target="$TARGET" --target-action=promote restore >&2
# Own socket directory and port, no archiving: the restored copy must never write into the live repository.
gosu postgres pg_ctl -D /restore/data -w -t 1800 -l /tmp/postgres.log \
  -o "-c archive_mode=off -c max_connections=$MAX_CONNECTIONS -c port=5433 -k /tmp -c listen_addresses=''" start >&2 \
  || { cat /tmp/postgres.log >&2; exit 1; }
q() { psql -h /tmp -p 5433 -U postgres -Atq "$@"; }
echo "markers=$(q -d restore_drill -c "select string_agg(label, ',' order by label) from marker where label like '%-$ID'")"
echo "orgs=$(q -d dovepeak -c 'select count(*) from organizations')"
echo "envs=$(q -d dovepeak -c 'select count(*) from environments')"
echo "realms=$(q -d keycloak -c 'select count(*) from realm')"
echo "users=$(q -d keycloak -c 'select count(*) from user_entity')"
echo "audit=$(q -d dovepeak -c 'select count(*) from audit_events')"
gosu postgres pg_ctl -D /restore/data -m fast stop >&2
RESTORE
)
duration=$(( $(date +%s) - started ))

value() { echo "$result" | sed -n "s/^$1=//p"; }
markers=$(value markers); orgs=$(value orgs); envs=$(value envs); realms=$(value realms); users=$(value users); audit=$(value audit)

passed=true
check() {
  if [ "$2" = "$3" ]; then echo "PASS  $1 ($2)"; else echo "FAIL  $1: expected $3, got $2"; passed=false; fi
}
check "point in time: only the marker written before the target" "$markers" "before-$id"
check "organizations restored" "$orgs" "$expected_orgs"
check "environments restored" "$envs" "$expected_envs"
check "Keycloak realms restored" "$realms" "$expected_realms"
check "Keycloak users restored" "$users" "$expected_users"
check "audit events restored" "$audit" "$expected_audit"
[ "$archive_lag" -le 900 ] && echo "PASS  RPO: archived within ${archive_lag}s (objective 900s)" || { echo "FAIL  RPO: ${archive_lag}s"; passed=false; }
[ "$duration" -le 3600 ] && echo "PASS  RTO (restore and recovery): ${duration}s (objective 3600s)" || { echo "FAIL  RTO: ${duration}s"; passed=false; }

echo
echo "| $(date -u +%Y-%m-%d) | ${archive_lag} s | ${duration} s | ${orgs} organizations, ${envs} environments, ${realms} realms, ${users} users, ${audit} audit events | $($passed && echo Passed || echo FAILED) |"
$passed
