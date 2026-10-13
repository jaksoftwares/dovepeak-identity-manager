#!/bin/sh
# pgBackRest rejects options that are set but empty. Compose passes every PGBACKREST_* variable (S3 settings are empty
# for a local repository), so drop the empty ones before running the real binary. Installed ahead of it on PATH, so
# archive_command, the backup scheduler and operators all go through it.
for var in $(env | sed -n 's/^\(PGBACKREST_[A-Z0-9_]*\)=$/\1/p'); do
  unset "$var"
done
exec /usr/bin/pgbackrest "$@"
