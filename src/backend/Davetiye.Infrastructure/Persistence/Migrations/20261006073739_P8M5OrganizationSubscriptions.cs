using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M5OrganizationSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_subscription_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    paid_through_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_boundary_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_settlement_applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_subscriptions", x => x.id);
                    table.CheckConstraint("ck_organization_subscriptions_cancellation_boundary_not_after_~", "cancellation_boundary_at_utc IS NULL OR cancellation_boundary_at_utc <= paid_through_at_utc");
                    table.CheckConstraint("ck_organization_subscriptions_cancellation_pairing", "cancel_at_period_end = (cancel_requested_at_utc IS NOT NULL AND cancellation_boundary_at_utc IS NOT NULL) AND ((cancel_requested_at_utc IS NULL) = (cancellation_boundary_at_utc IS NULL))");
                    table.CheckConstraint("ck_organization_subscriptions_provider_identity_nonblank", "length(btrim(provider_name)) > 0 AND length(btrim(provider_subscription_id)) > 0");
                    table.CheckConstraint("ck_organization_subscriptions_settlement_requires_cancellation", "NOT cancellation_settlement_applied OR cancel_at_period_end");
                });

            migrationBuilder.CreateTable(
                name: "organization_subscription_billing_cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_cycle_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    period_starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_failure_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_notice_recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    success_notice_recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_subscription_billing_cycles", x => x.id);
                    table.CheckConstraint("ck_organization_subscription_cycles_failure_notice_pairing", "(first_failure_at_utc IS NULL) = (failure_notice_recorded_at_utc IS NULL)");
                    table.CheckConstraint("ck_organization_subscription_cycles_period_order", "period_ends_at_utc > period_starts_at_utc");
                    table.CheckConstraint("ck_organization_subscription_cycles_provider_identity_nonblank", "length(btrim(provider_cycle_id)) > 0");
                    table.CheckConstraint("ck_organization_subscription_cycles_status_supported", "status IN ('Pending', 'Failed', 'Succeeded')");
                    table.CheckConstraint("ck_organization_subscription_cycles_status_timestamps", "(status = 'Pending' AND first_failure_at_utc IS NULL AND succeeded_at_utc IS NULL) OR (status = 'Failed' AND first_failure_at_utc IS NOT NULL AND succeeded_at_utc IS NULL) OR (status = 'Succeeded' AND succeeded_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_organization_subscription_cycles_success_notice_requires_su~", "success_notice_recorded_at_utc IS NULL OR succeeded_at_utc IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_organization_subscription_billing_cycles_organization_subsc~",
                        column: x => x.organization_subscription_id,
                        principalTable: "organization_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_subscription_cycles_subscription_period_start",
                table: "organization_subscription_billing_cycles",
                columns: new[] { "organization_subscription_id", "period_starts_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_organization_subscription_cycles_provider_identity",
                table: "organization_subscription_billing_cycles",
                columns: new[] { "organization_subscription_id", "provider_cycle_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_subscriptions_account_paid_through",
                table: "organization_subscriptions",
                columns: new[] { "account_id", "paid_through_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_subscriptions_canceled_paid_through",
                table: "organization_subscriptions",
                column: "paid_through_at_utc",
                filter: "cancel_at_period_end = true");

            migrationBuilder.CreateIndex(
                name: "ux_organization_subscriptions_provider_identity",
                table: "organization_subscriptions",
                columns: new[] { "provider_name", "provider_subscription_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_subscription_billing_cycles");

            migrationBuilder.DropTable(
                name: "organization_subscriptions");
        }
    }
}
