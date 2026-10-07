using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P3M2_EntitlementsAndGrantReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_account_plan_grants_account_id",
                table: "account_plan_grants");

            migrationBuilder.AddColumn<string>(
                name: "billing_kind",
                table: "plans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "OneTime");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "plans",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "TRY");

            migrationBuilder.AddColumn<decimal>(
                name: "price_amount",
                table: "plans",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_invitation_id",
                table: "account_plan_grants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consumed_at",
                table: "account_plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_at",
                table: "account_plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            // Existing installations may already contain operator-created plan rows. Backfill the
            // four accepted starter keys without deleting or replacing any row. Unknown plans are
            // deliberately deactivated so an upgrade cannot turn a legacy row into an active,
            // zero-price one-time offer before an operator reviews its commercial terms.
            migrationBuilder.Sql("""
                UPDATE plans
                SET billing_kind = CASE key
                        WHEN 'free' THEN 'Free'
                        WHEN 'organization' THEN 'Monthly'
                        ELSE 'OneTime'
                    END,
                    currency = 'TRY',
                    price_amount = CASE key
                        WHEN 'standard' THEN 699
                        WHEN 'premium' THEN 1199
                        WHEN 'organization' THEN 2499
                        ELSE 0
                    END,
                    is_active = CASE
                        WHEN key IN ('free', 'standard', 'premium', 'organization') THEN is_active
                        ELSE false
                    END;

                ALTER TABLE plans ALTER COLUMN billing_kind DROP DEFAULT;
                ALTER TABLE plans ALTER COLUMN currency DROP DEFAULT;
                ALTER TABLE plans ALTER COLUMN price_amount DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_billing_kind_supported",
                table: "plans",
                sql: "billing_kind IN ('Free', 'OneTime', 'Monthly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_currency_iso_length",
                table: "plans",
                sql: "currency ~ '^[A-Z]{3}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_free_price",
                table: "plans",
                sql: "billing_kind <> 'Free' OR price_amount = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_price_non_negative",
                table: "plans",
                sql: "price_amount >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_account_plan_grants_account_id_revoked_at_consumed_at",
                table: "account_plan_grants",
                columns: new[] { "account_id", "revoked_at", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_account_plan_grants_lifetime_free_per_account",
                table: "account_plan_grants",
                column: "account_id",
                unique: true,
                filter: "source = 'Free'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_plan_grants_consumption_order",
                table: "account_plan_grants",
                sql: "consumed_at IS NULL OR consumed_at >= reserved_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_plan_grants_consumption_requires_reservation",
                table: "account_plan_grants",
                sql: "consumed_at IS NULL OR reserved_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_plan_grants_reservation_pairing",
                table: "account_plan_grants",
                sql: "(assigned_invitation_id IS NULL) = (reserved_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_plan_grants_shared_grant_unassigned",
                table: "account_plan_grants",
                sql: "source <> 'OrganizationSubscription' OR (assigned_invitation_id IS NULL AND reserved_at IS NULL AND consumed_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_billing_kind_supported",
                table: "plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_currency_iso_length",
                table: "plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_free_price",
                table: "plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_price_non_negative",
                table: "plans");

            migrationBuilder.DropIndex(
                name: "ix_account_plan_grants_account_id_revoked_at_consumed_at",
                table: "account_plan_grants");

            migrationBuilder.DropIndex(
                name: "ux_account_plan_grants_lifetime_free_per_account",
                table: "account_plan_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_plan_grants_consumption_order",
                table: "account_plan_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_plan_grants_consumption_requires_reservation",
                table: "account_plan_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_plan_grants_reservation_pairing",
                table: "account_plan_grants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_plan_grants_shared_grant_unassigned",
                table: "account_plan_grants");

            migrationBuilder.DropColumn(
                name: "billing_kind",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "price_amount",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "assigned_invitation_id",
                table: "account_plan_grants");

            migrationBuilder.DropColumn(
                name: "consumed_at",
                table: "account_plan_grants");

            migrationBuilder.DropColumn(
                name: "reserved_at",
                table: "account_plan_grants");

            migrationBuilder.CreateIndex(
                name: "ix_account_plan_grants_account_id",
                table: "account_plan_grants",
                column: "account_id");
        }
    }
}
