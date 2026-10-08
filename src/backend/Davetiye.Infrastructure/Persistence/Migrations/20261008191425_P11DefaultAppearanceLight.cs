using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P11DefaultAppearanceLight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "preferred_appearance",
                table: "asp_net_users",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "light",
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6,
                oldDefaultValue: "system");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "preferred_appearance",
                table: "asp_net_users",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "system",
                oldClrType: typeof(string),
                oldType: "character varying(6)",
                oldMaxLength: 6,
                oldDefaultValue: "light");
        }
    }
}
