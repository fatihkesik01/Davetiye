using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9M4PlanBillingKindSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_kind_at_grant",
                table: "account_plan_grants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Grant source is the immutable accepted acquisition shape: historical plan catalog
            // edits must not rewrite the behavior already accepted by existing accounts.
            migrationBuilder.Sql("""
                UPDATE account_plan_grants
                SET billing_kind_at_grant = CASE source
                    WHEN 'Free' THEN 'Free'
                    WHEN 'IndividualPurchase' THEN 'OneTime'
                    WHEN 'OrganizationSubscription' THEN 'Monthly'
                    ELSE NULL
                END;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "billing_kind_at_grant",
                table: "account_plan_grants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_kind_at_grant",
                table: "account_plan_grants");
        }
    }
}
