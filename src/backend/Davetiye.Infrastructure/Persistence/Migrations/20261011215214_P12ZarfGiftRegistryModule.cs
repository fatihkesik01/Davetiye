using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P12ZarfGiftRegistryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only: template metadata is DB-managed after first insert (ADR-0007), so the
            // initializer never rewrites it. Only an untouched row (exactly the previous seed value)
            // is changed; an admin-edited module list is left alone.
            migrationBuilder.Sql(
                """
                UPDATE template_definitions
                SET supported_modules = '["hero","dateVenue","countdown","calendar","contact","rsvp","gallery","giftRegistry","qr"]'::jsonb, revision = revision + 1
                WHERE "key" = 'zarf-davetiye' AND supported_modules = '["hero","dateVenue","countdown","calendar","contact","rsvp","gallery","qr"]'::jsonb;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE template_definitions
                SET supported_modules = '["hero","dateVenue","countdown","calendar","contact","rsvp","gallery","qr"]'::jsonb, revision = revision + 1
                WHERE "key" = 'zarf-davetiye' AND supported_modules = '["hero","dateVenue","countdown","calendar","contact","rsvp","gallery","giftRegistry","qr"]'::jsonb;
                """);
        }
    }
}
