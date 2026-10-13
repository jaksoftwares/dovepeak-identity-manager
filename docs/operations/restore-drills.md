# Restore Drills

A backup is only as good as the last restore that worked. Every month an operator runs the restore drill and records the result here (milestone M6.2, acceptance criterion "database backup and restoration have been tested").

```bash
scripts/ops/restore-drill.sh
```

The drill never touches the live database. It writes a marker, notes the time, writes a second marker, waits until the WAL is archived, then restores the newest backup plus WAL to the noted time into a scratch volume and checks that:

* only the first marker exists (true point-in-time recovery, not just "the latest backup");
* the platform and Keycloak databases hold the same organizations, environments and realms as the live server;
* the WAL was archived within the RPO (15 minutes) and the restore finished within the RTO (1 hour).

The full procedure for a real failure, including restoring in place and on a new host, is in the [database failure runbook](../runbooks/database-failure.md).

## Results

| Date | Archive lag (RPO) | Restore and recovery (RTO) | Data checked | Result |
| ---- | ----------------- | -------------------------- | ------------ | ------ |
| 2026-10-10 | 3 s | 8 s | 0 organizations, 0 environments, 2 realms (empty rehearsal) | Passed |

### Findings from the first drills (2026-10-10)

The first attempts failed, and each failure would also have broken a real restore. All are fixed:

1. pgBackRest rejects options that are set but empty; the unused S3 settings broke WAL archiving. A wrapper drops empty `PGBACKREST_*` variables.
2. The `restore_command` pgBackRest writes calls the binary by absolute path, bypassing that wrapper, so recovery could not fetch WAL. `PGBACKREST_CMD` now points generated commands at the wrapper.
3. The Alpine pgBackRest package has no zstd support; WAL archiving failed until compression was switched to lz4. `WalArchivingFailing` exists to catch exactly this.
4. Recovery refuses to start with a lower `max_connections` than the primary; the drill now reads the live value.
