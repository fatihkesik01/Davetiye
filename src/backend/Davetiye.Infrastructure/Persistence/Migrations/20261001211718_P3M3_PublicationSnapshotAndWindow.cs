using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P3M3_PublicationSnapshotAndWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_code",
                table: "invitations",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                table: "invitations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Draft");

            // Existing Draft rows predate public locators. Two independent UUID v4 values provide
            // 244 random bits after fixed version/variant bits, comfortably above ADR-0002's
            // 128-bit minimum. New rows use the application CSPRNG's full 256-bit format.
            migrationBuilder.Sql("""
                UPDATE invitations
                SET public_code =
                    replace(gen_random_uuid()::text, '-', '') ||
                    replace(gen_random_uuid()::text, '-', ''),
                    state = 'Draft';

                ALTER TABLE invitations ALTER COLUMN public_code SET NOT NULL;
                ALTER TABLE invitations ALTER COLUMN state DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "publication_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publication_windows", x => x.id);
                    table.CheckConstraint("ck_publication_windows_interval", "starts_at < ends_at");
                    table.CheckConstraint("ck_publication_windows_timezone_not_blank", "length(btrim(time_zone_id)) > 0");
                    table.ForeignKey(
                        name: "fk_publication_windows_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "published_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    renderer_version = table.Column<int>(type: "integer", nullable: false),
                    content_schema_version = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    source_working_revision = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_published_contents", x => x.id);
                    table.CheckConstraint("ck_published_contents_renderer_version_positive", "renderer_version > 0");
                    table.CheckConstraint("ck_published_contents_schema_version_positive", "content_schema_version > 0");
                    table.CheckConstraint("ck_published_contents_source_revision_non_negative", "source_working_revision >= 0");
                    table.CheckConstraint("ck_published_contents_update_order", "updated_at >= published_at");
                    table.ForeignKey(
                        name: "fk_published_contents_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_invitations_public_code",
                table: "invitations",
                column: "public_code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invitations_public_code_format",
                table: "invitations",
                sql: "public_code ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invitations_state_supported",
                table: "invitations",
                sql: "state IN ('Draft', 'Scheduled', 'Active', 'Paused', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "ix_publication_windows_current_interval",
                table: "publication_windows",
                columns: new[] { "is_current", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_publication_windows_grant_id",
                table: "publication_windows",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ux_publication_windows_current_invitation",
                table: "publication_windows",
                column: "invitation_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_published_contents_invitation_id",
                table: "published_contents",
                column: "invitation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_windows");

            migrationBuilder.DropTable(
                name: "published_contents");

            migrationBuilder.DropIndex(
                name: "ux_invitations_public_code",
                table: "invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invitations_public_code_format",
                table: "invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invitations_state_supported",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "public_code",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "state",
                table: "invitations");
        }
    }
}
