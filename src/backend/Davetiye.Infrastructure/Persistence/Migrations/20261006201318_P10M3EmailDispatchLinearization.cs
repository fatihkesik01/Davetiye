using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P10M3EmailDispatchLinearization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_started_at_utc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_dispatch_processed_order",
                table: "outbox_messages",
                sql: "processed_at IS NULL OR dispatch_started_at_utc IS NULL OR processed_at >= dispatch_started_at_utc");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_dispatch_started_range",
                table: "outbox_messages",
                sql: "dispatch_started_at_utc IS NULL OR dispatch_started_at_utc >= created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM outbox_messages WHERE dispatch_started_at_utc IS NOT NULL) THEN " +
                "RAISE EXCEPTION 'P10-M3 dispatch history cannot be downgraded after external delivery was authorized'; " +
                "END IF; END $$;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_dispatch_processed_order",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_dispatch_started_range",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dispatch_started_at_utc",
                table: "outbox_messages");
        }
    }
}
