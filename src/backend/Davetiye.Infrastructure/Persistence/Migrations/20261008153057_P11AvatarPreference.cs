using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11AvatarPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_avatar",
                table: "asp_net_users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_avatar",
                table: "asp_net_users",
                sql: "preferred_avatar IS NULL OR preferred_avatar IN ('sunny', 'mint', 'berry', 'sky', 'coral', 'lilac', 'amber', 'forest', 'night', 'rose', 'slate', 'peach')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_avatar",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "preferred_avatar",
                table: "asp_net_users");
        }
    }
}
