using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roomy.Api.Migrations
{
    /// <inheritdoc />
    public partial class PoliciesApprovalsSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "no_show_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "settings",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booking_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_series", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_series_tenant_id_organizer_id",
                table: "booking_series",
                columns: new[] { "tenant_id", "organizer_id" });

            // Tenants created before this migration have no settings JSON; backfill defaults
            // so policy checks never see zeroed values.
            migrationBuilder.Sql("""
                UPDATE tenants SET settings =
                  '{"BookingWindowDays":60,"MaxDurationMinutes":480,"MinDurationMinutes":15,"MaxActiveBookingsPerUser":10,"CheckinGraceMinutes":10,"ApprovalExpiryHours":48}'
                WHERE settings IS NULL OR settings::text = '{}';
                """);

            // Tenant isolation (technical design §4) — standard three statements.
            migrationBuilder.Sql("""
                ALTER TABLE booking_series ENABLE ROW LEVEL SECURITY;
                ALTER TABLE booking_series FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON booking_series
                    USING (tenant_id = current_setting('app.tenant_id')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_series");

            migrationBuilder.DropColumn(
                name: "no_show_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "settings",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "bookings");
        }
    }
}
