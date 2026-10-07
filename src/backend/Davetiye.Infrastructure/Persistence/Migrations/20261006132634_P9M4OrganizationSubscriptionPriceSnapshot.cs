using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9M4OrganizationSubscriptionPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price_amount_at_activation",
                table: "organization_subscriptions",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Prices were not Admin-editable before this migration, so the current plan amount is
            // the correct immutable activation amount for every existing subscription.
            migrationBuilder.Sql("""
                UPDATE organization_subscriptions AS subscription
                SET price_amount_at_activation = plan.price_amount
                FROM plans AS plan
                WHERE plan.id = subscription.plan_id;
                """);
            migrationBuilder.AlterColumn<decimal>(
                name: "price_amount_at_activation",
                table: "organization_subscriptions",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(19,4)",
                oldPrecision: 19,
                oldScale: 4,
                oldDefaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organization_subscriptions_activation_price_positive",
                table: "organization_subscriptions",
                sql: "price_amount_at_activation > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_organization_subscriptions_activation_price_positive",
                table: "organization_subscriptions");

            migrationBuilder.DropColumn(
                name: "price_amount_at_activation",
                table: "organization_subscriptions");
        }
    }
}
