# Database Migration Operations

Status: **Phase 1 foundation**  
Owner: Database specialist

This runbook describes the single PostgreSQL migration path. It does not
define product schema or commercial seed values.

## Ownership

See `docs/PHASE_1_PLAN.md` §4 for the full ownership and serialization
rules (why `Davetiye.Infrastructure` owns the only `DbContext`/migration
assembly, why the API never calls `Migrate`/`EnsureCreated`, etc.).
Operationally: migration files are serialized through the Database
specialist — two agents must not generate or edit the model snapshot
concurrently.

## Configuration

Supply the connection string at runtime. Do not place credentials in tracked
`appsettings.*.json` files.

```powershell
$env:Database__ConnectionString = '<runtime PostgreSQL connection string>'
dotnet run --project tools\Davetiye.DatabaseMigrator
```

`Database:CommandTimeoutSeconds` defaults to 30 and accepts 1 through 300.
Production startup fails closed when the connection or database credentials
are absent.

## Creating a migration

Restore the pinned local tool, set a design-time runtime connection string,
then generate into the Infrastructure migration directory:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> `
  --project src\backend\Davetiye.Infrastructure `
  --startup-project tools\Davetiye.DatabaseMigrator `
  --context DavetiyeDbContext `
  --output-dir Persistence\Migrations
```

Inspect the generated migration and SQL before it is accepted. A migration
must not add hardcoded commercial plan values or secrets.

## Upgrade strategy

1. Prefer expand/contract changes: add nullable or backward-compatible schema,
   deploy compatible code, backfill separately, and only then remove obsolete
   schema in a later reviewed migration.
2. Flag data loss, table rewrites, long locks and irreversible transforms as
   destructive. They require explicit review and a tested recovery path.
3. Run the migrator as a one-shot step before API promotion. A migration
   failure stops promotion; the API does not attempt repair at startup.
4. Verify both empty-database migration and upgrade from the previously
   released migration to the latest migration against real PostgreSQL.
5. Re-running the migrator at the latest version must be idempotent.

The ordinary latest-version run (without `--target`) also reconciles the
code-owned `TemplateDefinition` catalog after migrations complete. This seed
is idempotent and is the only operational writer of
`CurrentRendererVersion`; a catalog/compiled-registry mismatch fails the
one-shot migration step. Explicit `--target` runs are migration-only so a
rollback or point-in-time test never queries a catalog table that may not yet
exist at that target.

For an explicit test or recovery target, the runner accepts
`--target <migration-name>`; `--target 0` returns a disposable database to the
pre-migration state. Production rollback must never be attempted before the
generated down operations and data-loss risk have been reviewed.
