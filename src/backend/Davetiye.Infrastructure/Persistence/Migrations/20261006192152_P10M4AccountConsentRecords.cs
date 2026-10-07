using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P10M4AccountConsentRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_consent_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_consent_records", x => x.id);
                    table.CheckConstraint("ck_account_consent_records_kind", "kind IN ('ServiceNoticeAcknowledgement', 'MarketingPreference')");
                    table.CheckConstraint("ck_account_consent_records_service_notice_granted", "kind <> 'ServiceNoticeAcknowledgement' OR granted = TRUE");
                    table.CheckConstraint("ck_account_consent_records_source", "source IN ('EmailPasswordSignup', 'GoogleSignup', 'AccountSettings', 'ExistingAccountAcknowledgement')");
                    table.CheckConstraint("ck_account_consent_records_version_nonblank", "length(version) BETWEEN 1 AND 100 AND btrim(version) <> ''");
                    table.ForeignKey(
                        name: "fk_account_consent_records_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_consent_records_account_id_kind_recorded_at",
                table: "account_consent_records",
                columns: new[] { "account_id", "kind", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_consent_records");
        }
    }
}
