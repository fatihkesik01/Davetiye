using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P8M2PaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    checkout_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    provider_checkout_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempts_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payment_attempts_currency_try", "currency = 'TRY'");
                    table.CheckConstraint("ck_payment_attempts_idempotency_key_length", "length(idempotency_key) BETWEEN 16 AND 100");
                    table.CheckConstraint("ck_payment_attempts_status_supported", "status IN ('Pending', 'Unknown', 'Failed', 'Canceled', 'Succeeded')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_account_id_created_at",
                table: "payment_attempts",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_provider_checkout_id",
                table: "payment_attempts",
                column: "provider_checkout_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_reference",
                table: "payment_attempts",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_account_purchase_idempotency",
                table: "payment_attempts",
                columns: new[] { "account_id", "invitation_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_account_purchase_pending",
                table: "payment_attempts",
                columns: new[] { "account_id", "invitation_id" },
                unique: true,
                filter: "status IN ('Pending', 'Unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_attempts");
        }
    }
}
