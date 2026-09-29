# PostgreSQL Backup and Restore Runbook

Status: **Phase 1 operational foundation**

This runbook is for the Davetiye Compose project only. It never operates on
Lora containers, volumes, networks, images, or databases.

## Preconditions

- Configure an encrypted off-site destination and its credentials outside Git.
  The destination/provider has not been selected; it is an operational
  prerequisite, not a value to invent in this repository.
- Keep the backup encryption key/recipient and destination credential only in
  host secret storage or the scheduler's protected environment.
- The job must monitor both dump and off-site transfer failure. A local VPS
  copy alone is not an off-site backup.

## Daily logical backup

Run from `/opt/davetiye` with the production `.env` loaded. The scheduler must
use a restricted account and an absolute destination path owned by Davetiye.

```bash
set -a
. ./.env
set +a
stamp=$(date -u +%Y%m%dT%H%M%SZ)
docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
  pg_dump --format=custom --no-owner --username="$POSTGRES_USER" "$POSTGRES_DB" \
  > "/var/backups/davetiye/davetiye-${stamp}.dump"
```

Encrypt and transfer the resulting dump to the configured off-site destination,
then verify the remote object checksum before marking the job successful.
Retention is configured by the backup operator; it must not remove the only
known-good restore point. Alert on a non-zero dump, encryption, transfer, or
verification exit code.

## Restore rehearsal

At least once before a production release and regularly thereafter, restore
into an isolated disposable PostgreSQL instance; never restore over the live
Davetiye database and never point any command at Lora.

```bash
createdb davetiye_restore_test
pg_restore --clean --if-exists --no-owner --dbname=davetiye_restore_test \
  /secure/path/davetiye-<timestamp>.dump
```

Verify migration history, application readiness against the restored database,
and a representative non-sensitive query. Record the timestamp, artifact
checksum, PostgreSQL version, elapsed time, result, and operator. Delete only
the named disposable restore database after the evidence is recorded.

## Recovery notes

- Stop only the explicitly named Davetiye Compose services before a confirmed
  recovery operation. Do not use host-wide Docker prune commands.
- Database recovery changes data and requires an approved incident/recovery
  procedure. This runbook does not authorize a production restore by itself.
- After any Davetiye maintenance action, perform the Lora read-only checks in
  `docs/DEPLOYMENT.md`.
