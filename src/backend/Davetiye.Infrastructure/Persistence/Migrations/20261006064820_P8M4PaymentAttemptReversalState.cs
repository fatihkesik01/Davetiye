using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M4PaymentAttemptReversalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_status_supported",
                table: "payment_attempts");

            migrationBuilder.AddColumn<string>(
                name: "reversal_kind",
                table: "payment_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reversed_at_utc",
                table: "payment_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_reversal_state_pairing",
                table: "payment_attempts",
                sql: "(reversed_at_utc IS NULL) = (reversal_kind IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_reversal_state_supported",
                table: "payment_attempts",
                sql: "(status = 'Reversed' AND reversed_at_utc IS NOT NULL AND reversal_kind IN ('FullRefund', 'FinalLostChargeback')) OR (status <> 'Reversed' AND reversed_at_utc IS NULL AND reversal_kind IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_status_supported",
                table: "payment_attempts",
                sql: "status IN ('Pending', 'Unknown', 'Failed', 'Canceled', 'Succeeded', 'Reversed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_reversal_state_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_reversal_state_supported",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_status_supported",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "reversal_kind",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "reversed_at_utc",
                table: "payment_attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_status_supported",
                table: "payment_attempts",
                sql: "status IN ('Pending', 'Unknown', 'Failed', 'Canceled', 'Succeeded')");
        }
    }
}
