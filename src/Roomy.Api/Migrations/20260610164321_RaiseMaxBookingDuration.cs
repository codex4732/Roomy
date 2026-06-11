using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roomy.Api.Migrations
{
    /// <inheritdoc />
    public partial class RaiseMaxBookingDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raise the max booking duration to 30 days for tenants still on the old
            // 8-hour default. Tenants with a customized value are left untouched.
            migrationBuilder.Sql("""
                UPDATE tenants
                SET settings = jsonb_set(settings, '{MaxDurationMinutes}', '43200')
                WHERE (settings ->> 'MaxDurationMinutes')::int = 480;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tenants
                SET settings = jsonb_set(settings, '{MaxDurationMinutes}', '480')
                WHERE (settings ->> 'MaxDurationMinutes')::int = 43200;
                """);
        }
    }
}
