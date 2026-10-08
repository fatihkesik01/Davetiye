using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11GoldColorTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive: widen the allow-list to six keys (adds 'gold'). No data change.
            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users",
                sql: "preferred_color_theme IN ('kutlio', 'sage', 'rose', 'ocean', 'plum', 'gold')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-safe rollback: rows already using 'gold' would violate the restored 5-key
            // constraint, so they fall back to the default 'kutlio' theme first.
            migrationBuilder.Sql("UPDATE asp_net_users SET preferred_color_theme = 'kutlio' WHERE preferred_color_theme = 'gold';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asp_net_users_preferred_color_theme",
                table: "asp_net_users",
                sql: "preferred_color_theme IN ('kutlio', 'sage', 'rose', 'ocean', 'plum')");
        }
    }
}
