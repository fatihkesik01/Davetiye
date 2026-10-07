using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P6M1_MemorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    emoji = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memories", x => x.id);
                    table.CheckConstraint("ck_memories_display_name_length", "display_name IS NULL OR (char_length(display_name) BETWEEN 1 AND 60 AND btrim(display_name) <> '')");
                    table.CheckConstraint("ck_memories_emoji_length", "emoji IS NULL OR (char_length(emoji) BETWEEN 1 AND 32 AND btrim(emoji) <> '')");
                    table.CheckConstraint("ck_memories_finalized_state", "(state IN ('Published', 'Hidden')) = (finalized_at IS NOT NULL)");
                    table.CheckConstraint("ck_memories_hidden_state", "(state = 'Hidden') = (hidden_at IS NOT NULL)");
                    table.CheckConstraint("ck_memories_revision_nonnegative", "revision >= 0");
                    table.CheckConstraint("ck_memories_state", "state IN ('PendingMedia', 'Published', 'Hidden', 'Abandoned')");
                    table.CheckConstraint("ck_memories_text_length", "text IS NULL OR (char_length(text) BETWEEN 1 AND 500 AND btrim(text) <> '')");
                    table.CheckConstraint("ck_memories_timestamps_ordered", "(finalized_at IS NULL OR finalized_at >= created_at) AND (hidden_at IS NULL OR hidden_at >= finalized_at)");
                });

            migrationBuilder.CreateTable(
                name: "memory_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_configurations", x => x.id);
                    table.CheckConstraint("ck_memory_configurations_revision_nonnegative", "revision >= 0");
                    table.CheckConstraint("ck_memory_configurations_updated_after_created", "updated_at >= created_at");
                    table.CheckConstraint("ck_memory_configurations_visibility", "visibility IN ('CreatorOnly', 'Public')");
                });

            migrationBuilder.CreateTable(
                name: "memory_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    memory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_media", x => x.id);
                    table.CheckConstraint("ck_memory_media_ordinal_range", "ordinal BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_memory_media_memories_memory_id",
                        column: x => x.memory_id,
                        principalTable: "memories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memory_upload_capabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    memory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hmac_key_version = table.Column<int>(type: "integer", nullable: false),
                    hmac_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_upload_capabilities", x => x.id);
                    table.CheckConstraint("ck_memory_upload_capabilities_consumed_after_created", "consumed_at IS NULL OR consumed_at >= created_at");
                    table.CheckConstraint("ck_memory_upload_capabilities_digest_length", "octet_length(hmac_digest) = 32");
                    table.CheckConstraint("ck_memory_upload_capabilities_expiry_window", "expires_at > created_at AND expires_at <= created_at + interval '15 minutes'");
                    table.CheckConstraint("ck_memory_upload_capabilities_key_version_positive", "hmac_key_version > 0");
                    table.CheckConstraint("ck_memory_upload_capabilities_purpose", "purpose = 'memory-upload'");
                    table.CheckConstraint("ck_memory_upload_capabilities_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_memory_upload_capabilities_memories_memory_id",
                        column: x => x.memory_id,
                        principalTable: "memories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memories_invitation_state_created_at",
                table: "memories",
                columns: new[] { "invitation_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_memory_configurations_invitation",
                table: "memory_configurations",
                column: "invitation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_memory_media_asset",
                table: "memory_media",
                column: "media_asset_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_memory_media_memory_ordinal",
                table: "memory_media",
                columns: new[] { "memory_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_memory_upload_capabilities_active_memory",
                table: "memory_upload_capabilities",
                column: "memory_id",
                unique: true,
                filter: "revoked_at IS NULL AND consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_memory_upload_capabilities_hmac_digest",
                table: "memory_upload_capabilities",
                column: "hmac_digest",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_configurations");

            migrationBuilder.DropTable(
                name: "memory_media");

            migrationBuilder.DropTable(
                name: "memory_upload_capabilities");

            migrationBuilder.DropTable(
                name: "memories");
        }
    }
}
