using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11UiPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_appearance",
                table: "asp_net_users",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddColumn<string>(
                name: "preferred_color_theme",
                table: "asp_net_users",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "kutlio");

            migrationBuilder.AddColumn<string>(
                name: "preferred_locale",
                table: "asp_net_users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_appearance",
                table: "asp_net_users",
                sql: "preferred_appearance IN ('system', 'light', 'dark')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users",
                sql: "preferred_color_theme IN ('kutlio', 'sage', 'rose', 'ocean', 'plum')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_locale",
                table: "asp_net_users",
                sql: "preferred_locale IN ('tr', 'en')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_appearance",
                table: "asp_net_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_locale",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "preferred_appearance",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "preferred_color_theme",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "preferred_locale",
                table: "asp_net_users");
        }
    }
}
