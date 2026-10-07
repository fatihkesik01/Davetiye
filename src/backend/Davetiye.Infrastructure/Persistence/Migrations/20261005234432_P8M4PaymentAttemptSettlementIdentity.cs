using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M4PaymentAttemptSettlementIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "granted_plan_grant_id",
                table: "payment_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_payment_id",
                table: "payment_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_granted_plan_grant_id",
                table: "payment_attempts",
                column: "granted_plan_grant_id",
                unique: true,
                filter: "granted_plan_grant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_provider_payment_id",
                table: "payment_attempts",
                column: "provider_payment_id",
                unique: true,
                filter: "provider_payment_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts",
                sql: "(provider_payment_id IS NULL) = (granted_plan_grant_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts",
                sql: "status <> 'Succeeded' OR (provider_payment_id IS NOT NULL AND granted_plan_grant_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payment_attempts_granted_plan_grant_id",
                table: "payment_attempts");

            migrationBuilder.DropIndex(
                name: "ux_payment_attempts_provider_payment_id",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_identity_pairing",
                table: "payment_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_success_requires_provider_identity",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "granted_plan_grant_id",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "provider_payment_id",
                table: "payment_attempts");
        }
    }
}
