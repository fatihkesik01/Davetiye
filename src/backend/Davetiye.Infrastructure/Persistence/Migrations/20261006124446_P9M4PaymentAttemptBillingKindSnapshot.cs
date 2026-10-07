using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P9M4PaymentAttemptBillingKindSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_kind_at_attempt",
                table: "payment_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Every historic PaymentAttempt was created exclusively for an active OneTime TRY
            // purchase. Preserve its checkout-time meaning without consulting the mutable plan row.
            migrationBuilder.Sql("UPDATE payment_attempts SET billing_kind_at_attempt = 'OneTime'");
            migrationBuilder.AlterColumn<string>(
                name: "billing_kind_at_attempt",
                table: "payment_attempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attempts_billing_kind_at_attempt",
                table: "payment_attempts",
                sql: "billing_kind_at_attempt = 'OneTime'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attempts_billing_kind_at_attempt",
                table: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "billing_kind_at_attempt",
                table: "payment_attempts");
        }
    }
}
