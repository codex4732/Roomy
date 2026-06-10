using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Identity;
using Roomy.Api.Locations;
using Roomy.Api.Rooms;
using Roomy.Api.Tenants;

namespace Roomy.Api.Common.Persistence;

/// <summary>
/// Development-only: migrates the database and seeds a demo tenant so the stack is
/// usable immediately after `docker compose up`. Idempotent — skips seeding when the
/// demo tenant already exists.
/// </summary>
public static class DevSeeder
{
    public const string DemoSlug = "demo";
    public const string AdminEmail = "admin@demo.test";
    public const string MemberEmail = "member@demo.test";
    public const string FacilityEmail = "facility@demo.test";
    public const string Password = "RoomyDemo123!";

    public static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RoomyDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder");

        await db.Database.MigrateAsync();

        if (await db.Tenants.AnyAsync(t => t.Slug == DemoSlug))
        {
            return;
        }

        var tenant = new Tenant { Slug = DemoSlug, Name = "Demo Corp" };
        db.Tenants.Add(tenant);

        scope.ServiceProvider.GetRequiredService<ITenantContext>().Bind(tenant.Id);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var admin = new User
        {
            TenantId = tenant.Id, Email = AdminEmail, Name = "Demo Admin", Role = UserRole.TenantAdmin,
        };
        admin.PasswordHash = hasher.HashPassword(admin, Password);

        var member = new User
        {
            TenantId = tenant.Id, Email = MemberEmail, Name = "Demo Member", Role = UserRole.Member,
        };
        member.PasswordHash = hasher.HashPassword(member, Password);

        var facility = new User
        {
            TenantId = tenant.Id, Email = FacilityEmail, Name = "Demo Facility Manager",
            Role = UserRole.FacilityManager,
        };
        facility.PasswordHash = hasher.HashPassword(facility, Password);
        db.Users.AddRange(admin, member, facility);

        var hq = new Location
        {
            TenantId = tenant.Id, Name = "London HQ", Timezone = "Europe/London",
            Address = "1 Demo Street, London",
        };
        var lab = new Location
        {
            TenantId = tenant.Id, Name = "Berlin Lab", Timezone = "Europe/Berlin",
            Address = "Demoallee 5, Berlin",
        };
        db.Locations.AddRange(hq, lab);

        db.Rooms.AddRange(
            new Room { TenantId = tenant.Id, LocationId = hq.Id, Name = "Boardroom", Capacity = 14, Floor = "5", RequiresApproval = true },
            new Room { TenantId = tenant.Id, LocationId = hq.Id, Name = "Thames", Capacity = 8, Floor = "3" },
            new Room { TenantId = tenant.Id, LocationId = hq.Id, Name = "Camden", Capacity = 4, Floor = "3" },
            new Room { TenantId = tenant.Id, LocationId = hq.Id, Name = "Phone Booth A", Capacity = 1, Floor = "2", CheckinRequired = false },
            new Room { TenantId = tenant.Id, LocationId = lab.Id, Name = "Spree", Capacity = 10, Floor = "1" },
            new Room { TenantId = tenant.Id, LocationId = lab.Id, Name = "Mitte", Capacity = 6, Floor = "2" });

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded demo tenant '{Slug}' — log in with {Admin} or {Member} / {Password}",
            DemoSlug, AdminEmail, MemberEmail, Password);
    }
}
