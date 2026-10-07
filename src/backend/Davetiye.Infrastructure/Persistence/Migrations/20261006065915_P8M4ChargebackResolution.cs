using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M4ChargebackResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chargeback_resolution",
                table: "payment_attempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "chargeback_resolved_at_utc",
                table: "payment_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_chargeback_resolution_pairing",
                table: "payment_attempts",
                sql: "(chargeback_resolved_at_utc IS NULL) = (chargeback_resolution IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_chargeback_resolution_supported",
                table: "payment_attempts",
                sql: "chargeback_resolution IS NULL OR chargeback_resolution IN ('FinalWon', 'FinalLost')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_lost_chargeback_reversed",
                table: "payment_attempts",
                sql: "chargeback_resolution <> 'FinalLost' OR (status = 'Reversed' AND reversal_kind = 'FinalLostChargeback')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_chargeback_resolution_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_chargeback_resolution_supported",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_lost_chargeback_reversed",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "chargeback_resolution",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "chargeback_resolved_at_utc",
                table: "payment_attempts");
        }
    }
}
