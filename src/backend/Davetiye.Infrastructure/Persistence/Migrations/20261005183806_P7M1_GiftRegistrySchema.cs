using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P7M1_GiftRegistrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gift_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_items", x => x.id);
                    table.UniqueConstraint("ak_gift_items_id_invitation", x => new { x.id, x.invitation_id });
                    table.CheckConstraint("ck_gift_items_name", "char_length(name) BETWEEN 1 AND 200 AND btrim(name) <> ''");
                    table.CheckConstraint("ck_gift_items_ordinal_nonnegative", "ordinal >= 0");
                    table.CheckConstraint("ck_gift_items_requested_quantity_positive", "requested_quantity > 0");
                    table.CheckConstraint("ck_gift_items_revision_nonnegative", "revision >= 0");
                    table.CheckConstraint("ck_gift_items_updated_after_created", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "guest_gift_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hmac_key_version = table.Column<int>(type: "integer", nullable: false),
                    hmac_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guest_gift_sessions", x => x.id);
                    table.UniqueConstraint("ak_guest_gift_sessions_id_invitation", x => new { x.id, x.invitation_id });
                    table.CheckConstraint("ck_guest_gift_sessions_digest_length", "octet_length(hmac_digest) = 32");
                    table.CheckConstraint("ck_guest_gift_sessions_key_version_positive", "hmac_key_version > 0");
                    table.CheckConstraint("ck_guest_gift_sessions_purpose", "purpose = 'gift-session'");
                    table.CheckConstraint("ck_guest_gift_sessions_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "gift_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gift_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_gift_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    guest_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_reservations", x => x.id);
                    table.CheckConstraint("ck_gift_reservations_email_length", "email IS NULL OR char_length(email) BETWEEN 3 AND 320");
                    table.CheckConstraint("ck_gift_reservations_guest_full_name", "char_length(guest_full_name) BETWEEN 1 AND 200 AND btrim(guest_full_name) <> ''");
                    table.CheckConstraint("ck_gift_reservations_phone_length", "phone IS NULL OR char_length(phone) BETWEEN 1 AND 32");
                    table.CheckConstraint("ck_gift_reservations_quantity_positive", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_gift_reservations_gift_items_gift_item_id_invitation_id",
                        columns: x => new { x.gift_item_id, x.invitation_id },
                        principalTable: "gift_items",
                        principalColumns: new[] { "id", "invitation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gift_reservations_gift_session",
                        columns: x => new { x.guest_gift_session_id, x.invitation_id },
                        principalTable: "guest_gift_sessions",
                        principalColumns: new[] { "id", "invitation_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gift_items_invitation_ordinal",
                table: "gift_items",
                columns: new[] { "invitation_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservations_gift_item_id_invitation_id",
                table: "gift_reservations",
                columns: new[] { "gift_item_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservations_guest_gift_session_id_invitation_id",
                table: "gift_reservations",
                columns: new[] { "guest_gift_session_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservations_invitation",
                table: "gift_reservations",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_guest_gift_sessions_invitation_revoked_at",
                table: "guest_gift_sessions",
                columns: new[] { "invitation_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ux_guest_gift_sessions_hmac_digest",
                table: "guest_gift_sessions",
                column: "hmac_digest",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_reservations");

            migrationBuilder.DropTable(
                name: "gift_items");

            migrationBuilder.DropTable(
                name: "guest_gift_sessions");
        }
    }
}
