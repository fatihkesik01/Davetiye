using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4M4_MediaVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads");

            migrationBuilder.AddColumn<long>(
                name: "maximum_byte_length",
                table: "pending_uploads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "maximum_duration_seconds",
                table: "pending_uploads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Earlier uploads predate the persisted entitlement snapshot. Backfill their existing
            // declared length as the maximum so this schema change remains deployable; they remain
            // unable to finalize videos without a captured positive duration ceiling.
            migrationBuilder.Sql("UPDATE pending_uploads SET maximum_byte_length = declared_byte_length WHERE maximum_byte_length < declared_byte_length;");

            migrationBuilder.CreateTable(
                name: "media_provider_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_provider_events", x => x.id);
                    table.CheckConstraint("ck_media_provider_events_fingerprint", "length(event_fingerprint) = 64");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads",
                sql: "declared_byte_length > 0 AND maximum_byte_length >= declared_byte_length AND maximum_duration_seconds >= 0 AND length(request_fingerprint) = 64 AND requested_presentation_role IN ('Cover', 'Gallery')");

            migrationBuilder.CreateIndex(
                name: "ix_media_provider_events_event_fingerprint",
                table: "media_provider_events",
                column: "event_fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_provider_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "maximum_byte_length",
                table: "pending_uploads");

            migrationBuilder.DropColumn(
                name: "maximum_duration_seconds",
                table: "pending_uploads");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pending_uploads_request_values",
                table: "pending_uploads",
                sql: "declared_byte_length > 0 AND length(request_fingerprint) = 64 AND requested_presentation_role IN ('Cover', 'Gallery')");
        }
    }
}
