using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4M1_CreatorMediaContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quota_scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    provider_object_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    detected_content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    byte_length = table.Column<long>(type: "bigint", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deletion_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_assets", x => x.id);
                    table.CheckConstraint("ck_media_assets_deleted_timestamp", "state <> 'Deleted' OR provider_deleted_at IS NOT NULL");
                    table.CheckConstraint("ck_media_assets_deletion_timestamps", "state NOT IN ('PendingDeletion', 'Deleted') OR deletion_requested_at IS NOT NULL");
                    table.CheckConstraint("ck_media_assets_positive_measurements", "(byte_length IS NULL OR byte_length > 0) AND (duration_seconds IS NULL OR duration_seconds > 0)");
                    table.CheckConstraint("ck_media_assets_ready_metadata", "state <> 'Ready' OR (provider_object_reference IS NOT NULL AND detected_content_type IS NOT NULL AND byte_length > 0 AND ready_at IS NOT NULL)");
                    table.CheckConstraint("ck_media_assets_supported_values", "quota_scope IN ('Creator', 'Guest') AND kind IN ('Image', 'Video') AND state IN ('PendingUpload', 'Processing', 'Ready', 'Rejected', 'PendingDeletion', 'Deleted')");
                    table.ForeignKey(
                        name: "fk_media_assets_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_placements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_placements", x => x.id);
                    table.CheckConstraint("ck_media_placements_nonnegative_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_media_placements_supported_role", "role IN ('Cover', 'Gallery')");
                    table.ForeignKey(
                        name: "fk_media_placements_media_assets_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_uploads", x => x.id);
                    table.CheckConstraint("ck_pending_uploads_expiry_after_creation", "created_at < expires_at");
                    table.CheckConstraint("ck_pending_uploads_terminal_state_pairing", "consumed_at IS NULL OR cancelled_at IS NULL");
                    table.ForeignKey(
                        name: "fk_pending_uploads_media_assets_media_asset_id",
                        column: x => x.media_asset_id,
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_invitation_quota_kind_state",
                table: "media_assets",
                columns: new[] { "invitation_id", "quota_scope", "kind", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_state_created_at",
                table: "media_assets",
                columns: new[] { "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_media_placements_role_sort_order",
                table: "media_placements",
                columns: new[] { "role", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_media_placements_asset_role",
                table: "media_placements",
                columns: new[] { "media_asset_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_uploads_open_expiry",
                table: "pending_uploads",
                columns: new[] { "consumed_at", "cancelled_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pending_uploads_one_open_per_asset",
                table: "pending_uploads",
                column: "media_asset_id",
                unique: true,
                filter: "consumed_at IS NULL AND cancelled_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_placements");

            migrationBuilder.DropTable(
                name: "pending_uploads");

            migrationBuilder.DropTable(
                name: "media_assets");
        }
    }
}
