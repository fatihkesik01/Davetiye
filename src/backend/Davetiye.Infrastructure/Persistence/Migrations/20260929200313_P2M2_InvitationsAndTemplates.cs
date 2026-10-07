using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P2M2_InvitationsAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    renderer_version = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.CheckConstraint("ck_invitations_template_pin_pairing", "(template_key IS NULL) = (renderer_version IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "template_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_premium = table.Column<bool>(type: "boolean", nullable: false),
                    current_renderer_version = table.Column<int>(type: "integer", nullable: false),
                    preview_image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    supported_modules = table.Column<string>(type: "jsonb", nullable: false),
                    required_fields = table.Column<string>(type: "jsonb", nullable: false),
                    recommended_fields = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "working_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_schema_version = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_working_contents_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalTable: "invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_account_id",
                table: "invitations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_definitions_is_active",
                table: "template_definitions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_template_definitions_key",
                table: "template_definitions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_working_contents_invitation_id",
                table: "working_contents",
                column: "invitation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_definitions");

            migrationBuilder.DropTable(
                name: "working_contents");

            migrationBuilder.DropTable(
                name: "invitations");
        }
    }
}
