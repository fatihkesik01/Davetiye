using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9M2AdminBanAuditSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ban_records_accounts_account_id",
                table: "ban_records");

            migrationBuilder.AddColumn<string>(
                name: "internal_note",
                table: "ban_records",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_audit_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_records", x => x.id);
                    table.CheckConstraint("ck_admin_audit_records_event_type_nonblank", "length(btrim(event_type)) BETWEEN 1 AND 100");
                });

            migrationBuilder.CreateIndex(
                name: "ux_ban_records_one_active_per_account",
                table: "ban_records",
                column: "account_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ban_records_internal_note_nonblank",
                table: "ban_records",
                sql: "internal_note IS NULL OR (length(internal_note) <= 2000 AND btrim(internal_note) <> '')");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_actor_time",
                table: "admin_audit_records",
                columns: new[] { "actor_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_subject_time",
                table: "admin_audit_records",
                columns: new[] { "subject_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_records_time_id",
                table: "admin_audit_records",
                columns: new[] { "occurred_at_utc", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_ban_records_accounts_account_id",
                table: "ban_records",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ban_records_accounts_account_id",
                table: "ban_records");

            migrationBuilder.DropTable(
                name: "admin_audit_records");

            migrationBuilder.DropIndex(
                name: "ux_ban_records_one_active_per_account",
                table: "ban_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ban_records_internal_note_nonblank",
                table: "ban_records");

            migrationBuilder.DropColumn(
                name: "internal_note",
                table: "ban_records");

            migrationBuilder.AddForeignKey(
                name: "fk_ban_records_accounts_account_id",
                table: "ban_records",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
