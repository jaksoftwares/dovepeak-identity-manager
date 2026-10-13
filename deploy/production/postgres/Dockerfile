# PostgreSQL with pgBackRest for continuous WAL archiving and point-in-time recovery (milestone M6.2).
# Same major version and base image (Alpine, musl collation) as the development stack.
FROM postgres:17.11-alpine

RUN apk add --no-cache pgbackrest jq busybox-extras \
    && mkdir -p /var/lib/pgbackrest /var/log/pgbackrest /var/spool/pgbackrest \
    && chown -R postgres:postgres /var/lib/pgbackrest /var/log/pgbackrest /var/spool/pgbackrest

COPY backup-scheduler.sh /usr/local/bin/backup-scheduler.sh
COPY pgbackrest-wrapper.sh /usr/local/bin/pgbackrest
RUN chmod 0755 /usr/local/bin/backup-scheduler.sh /usr/local/bin/pgbackrest
