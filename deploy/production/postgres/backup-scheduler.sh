#!/bin/sh
# Backup scheduler for the pg-backup container (milestone M6.2).
#
# Creates the pgBackRest stanza, then keeps backups current: a full backup when none exists or the newest full is
# older than FULL_BACKUP_DAYS, otherwise a differential every DIFF_BACKUP_HOURS. Point-in-time recovery between
# backups comes from WAL archiving (archive_command in the postgres service, forced at least every 60 seconds).
#
# Metrics for alerting are served on :9187/metrics (internal network only): age of the newest backup and of the newest
# archived WAL segment, which bounds the data that a restore would lose (RPO).
#
# All pgBackRest settings come from PGBACKREST_* environment variables (repository, encryption, retention).
set -eu

STANZA="${PGBACKREST_STANZA:-dovepeak}"
FULL_DAYS="${FULL_BACKUP_DAYS:-7}"
DIFF_HOURS="${DIFF_BACKUP_HOURS:-24}"
METRICS_DIR=/tmp/backup-metrics

log() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $*"; }

mkdir -p "$METRICS_DIR"
echo "# no data yet" > "$METRICS_DIR/metrics"
httpd -p 9187 -h "$METRICS_DIR"

until pg_isready -h /var/run/postgresql -q; do
  log "waiting for PostgreSQL"
  sleep 5
done

log "ensuring stanza '$STANZA'"
pgbackrest --stanza="$STANZA" stanza-create
# A failing check (usually WAL archiving) must not stop the scheduler: it keeps exporting metrics so the
# WalArchivingFailing alert fires; PostgreSQL itself keeps retrying the archive.
pgbackrest --stanza="$STANZA" check || log "check FAILED: WAL archiving is not working"

# Unix time the newest backup of a type ("full", or "any") finished, or 0 when there is none.
newest() {
  pgbackrest --stanza="$STANZA" --output=json info \
    | jq --arg type "$1" '[.[0].backup[]? | select($type == "any" or .type == $type) | .timestamp.stop] | max // 0'
}

while true; do
  now=$(date +%s)
  if [ $(( now - $(newest full) )) -ge $(( FULL_DAYS * 86400 )) ]; then
    log "starting full backup"
    pgbackrest --stanza="$STANZA" --type=full backup && log "full backup complete" || log "full backup FAILED"
  elif [ $(( now - $(newest any) )) -ge $(( DIFF_HOURS * 3600 )) ]; then
    log "starting differential backup"
    pgbackrest --stanza="$STANZA" --type=diff backup && log "differential backup complete" || log "differential backup FAILED"
  fi

  archived=$(psql -h /var/run/postgresql -U postgres -d postgres -Atc \
    "select coalesce(extract(epoch from last_archived_time)::bigint, 0) || ' ' || failed_count from pg_stat_archiver" || echo "0 0")
  set -- $archived
  {
    echo "# TYPE dovepeak_backup_last_success_timestamp_seconds gauge"
    echo "dovepeak_backup_last_success_timestamp_seconds $(newest any)"
    echo "# TYPE dovepeak_wal_last_archived_timestamp_seconds gauge"
    echo "dovepeak_wal_last_archived_timestamp_seconds $1"
    echo "# TYPE dovepeak_wal_archive_failures_total counter"
    echo "dovepeak_wal_archive_failures_total $2"
  } > "$METRICS_DIR/metrics.tmp" && mv "$METRICS_DIR/metrics.tmp" "$METRICS_DIR/metrics"

  sleep 60
done
