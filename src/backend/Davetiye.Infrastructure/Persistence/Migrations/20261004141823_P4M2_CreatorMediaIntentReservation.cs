using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4M2_CreatorMediaIntentReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "declared_byte_length",
                table: "pending_uploads",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "idempotency_key",
                table: "pending_uploads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_fingerprint",
                table: "pending_uploads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requested_presentation_role",
                table: "pending_uploads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Rows created by the earlier P4M1 schema have no replay metadata. They cannot safely
            // be resumed under the new idempotency contract, so close their short-lived capability
            // reservation and backfill deterministic metadata that remains unique and constraint-
            // valid. These placeholder request values are only for terminal legacy rows.
            migrationBuilder.Sql(
                "UPDATE media_assets AS asset " +
                "SET state = 'Rejected', revision = asset.revision + 1 " +
                "FROM pending_uploads AS intent " +
                "WHERE asset.id = intent.media_asset_id " +
                "  AND asset.state = 'PendingUpload' " +
                "  AND intent.consumed_at IS NULL AND intent.cancelled_at IS NULL " +
                "  AND (intent.idempotency_key IS NULL OR intent.requested_presentation_role IS NULL " +
                "       OR intent.declared_byte_length IS NULL OR intent.request_fingerprint IS NULL);");

            migrationBuilder.Sql(
                "UPDATE pending_uploads " +
                "SET idempotency_key = id, " +
                "    requested_presentation_role = 'Gallery', " +
                "    declared_byte_length = 1, " +
                "    request_fingerprint = md5(id::text) || md5(id::text), " +
                "    revision = revision + 1, " +
                "    cancelled_at = CASE " +
                "        WHEN consumed_at IS NULL AND cancelled_at IS NULL THEN CURRENT_TIMESTAMP " +
                "        ELSE cancelled_at " +
                "    END " +
                "WHERE idempotency_key IS NULL OR requested_presentation_role IS NULL " +
                "   OR declared_byte_length IS NULL OR request_fingerprint IS NULL;");

            migrationBuilder.AlterColumn<long>(
                name: "declared_byte_length",
                table: "pending_uploads",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "idempotency_key",
                table: "pending_uploads",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_fingerprint",
                table: "pending_uploads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "requested_presentation_role",
                table: "pending_uploads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_pending_uploads_idempotency_key",
                table: "pending_uploads",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads",
                sql: "declared_byte_length > 0 AND length(request_fingerprint) = 64 AND requested_presentation_role IN ('Cover', 'Gallery')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_pending_uploads_idempotency_key",
                table: "pending_uploads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "declared_byte_length",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "request_fingerprint",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "requested_presentation_role",
                table: "pending_uploads");
        }
    }
}
