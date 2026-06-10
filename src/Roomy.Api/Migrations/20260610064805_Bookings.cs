using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roomy.Api.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attendee_count = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bookings_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bookings_users_organizer_id",
                        column: x => x.organizer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_organizer_id",
                table: "bookings",
                column: "organizer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_room_id",
                table: "bookings",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_organizer_id_start_at",
                table: "bookings",
                columns: new[] { "tenant_id", "organizer_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_room_id_start_at",
                table: "bookings",
                columns: new[] { "tenant_id", "room_id", "start_at" });

            // Tenant isolation (technical design §4) — same three statements as every
            // tenant-owned table in InitialSchema.
            migrationBuilder.Sql("""
                ALTER TABLE bookings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE bookings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON bookings
                    USING (tenant_id = current_setting('app.tenant_id')::uuid);
                """);

            // No-double-booking invariant (FR-4.3, technical design §6.1): the database,
            // not the application, guarantees that slot-blocking bookings on the same room
            // never overlap. Status values 0/1/2 = PendingApproval/Confirmed/CheckedIn.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE bookings ADD COLUMN period tstzrange
                    GENERATED ALWAYS AS (tstzrange(start_at, end_at, '[)')) STORED;

                ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
                    EXCLUDE USING gist (room_id WITH =, period WITH &&)
                    WHERE (status IN (0, 1, 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
