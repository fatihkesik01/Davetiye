using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P10M3AccountDeletionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts");

            migrationBuilder.AddColumn<string>(
                name: "settlement_disposition",
                table: "payment_attempts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_account_id",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewal_cancellation_completed_at_utc",
                table: "organization_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewal_cancellation_requested_at_utc",
                table: "organization_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_completed_at_utc",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_started_at_utc",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            // Preserve existing successful/reversed settlements as entitlement-backed records.
            migrationBuilder.Sql(
                "UPDATE payment_attempts SET settlement_disposition = 'Granted' " +
                "WHERE status IN ('Succeeded', 'Reversed') AND provider_payment_id IS NOT NULL " +
                "AND granted_plan_grant_id IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "account_deletion_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_deletion_requests", x => x.id);
                    table.CheckConstraint("ck_account_deletion_requests_consumed_pairing", "(status = 'Consumed') = (consumed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_account_deletion_requests_consumed_range", "consumed_at_utc IS NULL OR (consumed_at_utc >= created_at_utc AND consumed_at_utc < expires_at_utc)");
                    table.CheckConstraint("ck_account_deletion_requests_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_account_deletion_requests_purpose", "purpose = 'AccountDeletion'");
                    table.CheckConstraint("ck_account_deletion_requests_status", "status IN ('Pending', 'Consumed', 'Superseded', 'Expired')");
                    table.CheckConstraint("ck_account_deletion_requests_token_hash", "length(token_hash) = 64 AND token_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_account_deletion_requests_updated_range", "updated_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "fk_account_deletion_requests_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_deletion_works",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    subscription_cancellations_queued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invitations_purge_queued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    identity_sanitized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_error_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_deletion_works", x => x.id);
                    table.CheckConstraint("ck_account_deletion_works_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_account_deletion_works_checkpoint_order", "(subscription_cancellations_queued_at_utc IS NULL OR subscription_cancellations_queued_at_utc >= started_at_utc) AND (invitations_purge_queued_at_utc IS NULL OR invitations_purge_queued_at_utc >= started_at_utc) AND (identity_sanitized_at_utc IS NULL OR identity_sanitized_at_utc >= started_at_utc) AND (completed_at_utc IS NULL OR completed_at_utc >= started_at_utc)");
                    table.CheckConstraint("ck_account_deletion_works_completion_checkpoints", "completed_at_utc IS NULL OR (subscription_cancellations_queued_at_utc IS NOT NULL AND invitations_purge_queued_at_utc IS NOT NULL AND identity_sanitized_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_account_deletion_works_completion_pairing", "(status = 'Completed') = (completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_account_deletion_works_error_kind", "last_error_kind IS NULL OR (length(last_error_kind) BETWEEN 1 AND 64 AND last_error_kind ~ '^[A-Za-z0-9._-]+$')");
                    table.CheckConstraint("ck_account_deletion_works_retry_pairing", "(status = 'Retrying') = (next_attempt_at_utc IS NOT NULL AND last_error_kind IS NOT NULL AND last_error_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_account_deletion_works_status", "status IN ('Queued', 'Retrying', 'Completed')");
                    table.ForeignKey(
                        name: "fk_account_deletion_works_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_grant_disposition_pairing",
                table: "payment_attempts",
                sql: "(settlement_disposition IS NULL AND granted_plan_grant_id IS NULL) OR (settlement_disposition = 'Granted' AND granted_plan_grant_id IS NOT NULL) OR (settlement_disposition = 'NoEntitlement' AND granted_plan_grant_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_settlement_disposition_supported",
                table: "payment_attempts",
                sql: "settlement_disposition IS NULL OR settlement_disposition IN ('Granted', 'NoEntitlement')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts",
                sql: "(settlement_disposition IS NULL) = (provider_payment_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts",
                sql: "status NOT IN ('Succeeded', 'Reversed') OR (provider_payment_id IS NOT NULL AND settlement_disposition IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_owner_account_processed",
                table: "outbox_messages",
                columns: new[] { "owner_account_id", "processed_at" },
                filter: "owner_account_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_subscriptions_renewal_cancellation_completion",
                table: "organization_subscriptions",
                sql: "renewal_cancellation_completed_at_utc IS NULL OR (renewal_cancellation_requested_at_utc IS NOT NULL AND renewal_cancellation_completed_at_utc >= renewal_cancellation_requested_at_utc)");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_deletion_started_at_utc",
                table: "accounts",
                column: "deletion_started_at_utc",
                filter: "deletion_started_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_deletion_timestamps",
                table: "accounts",
                sql: "deletion_completed_at_utc IS NULL OR (deletion_started_at_utc IS NOT NULL AND deletion_completed_at_utc >= deletion_started_at_utc)");

            migrationBuilder.CreateIndex(
                name: "ix_account_deletion_requests_account_id_created_at_utc",
                table: "account_deletion_requests",
                columns: new[] { "account_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_account_deletion_requests_token_hash",
                table: "account_deletion_requests",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_account_deletion_requests_one_pending_per_account",
                table: "account_deletion_requests",
                column: "account_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_account_deletion_works_account_id",
                table: "account_deletion_works",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_deletion_works_status_next_attempt_at_utc",
                table: "account_deletion_works",
                columns: new[] { "status", "next_attempt_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This schema cannot be rolled back without losing settlement-without-entitlement evidence.
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM payment_attempts WHERE settlement_disposition = 'NoEntitlement') " +
                "OR EXISTS (SELECT 1 FROM account_deletion_requests) " +
                "OR EXISTS (SELECT 1 FROM account_deletion_works) " +
                "OR EXISTS (SELECT 1 FROM accounts WHERE deletion_started_at_utc IS NOT NULL OR deletion_completed_at_utc IS NOT NULL) " +
                "OR EXISTS (SELECT 1 FROM organization_subscriptions WHERE renewal_cancellation_requested_at_utc IS NOT NULL) " +
                "OR EXISTS (SELECT 1 FROM outbox_messages WHERE owner_account_id IS NOT NULL) THEN " +
                "RAISE EXCEPTION 'P10-M3 cannot be downgraded while account-deletion or settlement evidence exists'; " +
                "END IF; END $$;");

            migrationBuilder.DropTable(
                name: "account_deletion_requests");

            migrationBuilder.DropTable(
                name: "account_deletion_works");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_grant_disposition_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_settlement_disposition_supported",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_owner_account_processed",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_subscriptions_renewal_cancellation_completion",
                table: "organization_subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_accounts_deletion_started_at_utc",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_deletion_timestamps",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "settlement_disposition",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "owner_account_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "renewal_cancellation_completed_at_utc",
                table: "organization_subscriptions");

            migrationBuilder.DropColumn(
                name: "renewal_cancellation_requested_at_utc",
                table: "organization_subscriptions");

            migrationBuilder.DropColumn(
                name: "deletion_completed_at_utc",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "deletion_started_at_utc",
                table: "accounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts",
                sql: "(provider_payment_id IS NULL) = (granted_plan_grant_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts",
                sql: "status <> 'Succeeded' OR (provider_payment_id IS NOT NULL AND granted_plan_grant_id IS NOT NULL)");
        }
    }
}
