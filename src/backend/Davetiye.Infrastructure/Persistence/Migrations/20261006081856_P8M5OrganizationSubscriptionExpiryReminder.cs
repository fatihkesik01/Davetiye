using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M5OrganizationSubscriptionExpiryReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organization_subscriptions_canceled_paid_through",
                table: "organization_subscriptions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "access_expiry_reminder_queued_at_utc",
                table: "organization_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "access_expiry_reminder_retry_after_utc",
                table: "organization_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_subscriptions_canceled_paid_through",
                table: "organization_subscriptions",
                column: "paid_through_at_utc",
                filter: "cancel_at_period_end = true AND access_expiry_reminder_queued_at_utc IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_subscriptions_expiry_reminder_pairing",
                table: "organization_subscriptions",
                sql: "access_expiry_reminder_queued_at_utc IS NULL OR (cancel_at_period_end AND access_expiry_reminder_queued_at_utc < paid_through_at_utc)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_subscriptions_expiry_reminder_retry_pairing",
                table: "organization_subscriptions",
                sql: "access_expiry_reminder_retry_after_utc IS NULL OR (cancel_at_period_end AND access_expiry_reminder_queued_at_utc IS NULL AND access_expiry_reminder_retry_after_utc < paid_through_at_utc)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organization_subscriptions_canceled_paid_through",
                table: "organization_subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_subscriptions_expiry_reminder_pairing",
                table: "organization_subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_subscriptions_expiry_reminder_retry_pairing",
                table: "organization_subscriptions");

            migrationBuilder.DropColumn(
                name: "access_expiry_reminder_queued_at_utc",
                table: "organization_subscriptions");

            migrationBuilder.DropColumn(
                name: "access_expiry_reminder_retry_after_utc",
                table: "organization_subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_organization_subscriptions_canceled_paid_through",
                table: "organization_subscriptions",
                column: "paid_through_at_utc",
                filter: "cancel_at_period_end = true");
        }
    }
}
