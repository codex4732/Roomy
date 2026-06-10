using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Identity;
using Roomy.Api.Locations;
using Roomy.Api.Rooms;
using Roomy.Api.Tenants;

namespace Roomy.Api.Common.Persistence;

public sealed class RoomyDbContext(DbContextOptions<RoomyDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Bookings.Booking> Bookings => Set<Bookings.Booking>();
    public DbSet<Bookings.BookingSeries> BookingSeries => Set<Bookings.BookingSeries>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // `tenants` is a platform table: no TenantId column, no query filter, no RLS.
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.Property(t => t.Slug).HasMaxLength(40);
            tenant.Property(t => t.Name).HasMaxLength(200);
            tenant.OwnsOne(t => t.Settings, s => s.ToJson());
        });

        modelBuilder.Entity<Bookings.BookingSeries>(series =>
        {
            series.HasIndex(s => new { s.TenantId, s.OrganizerId });
            series.Property(s => s.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            token.HasIndex(t => new { t.TenantId, t.TokenHash });
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.HasOne<User>().WithMany().HasForeignKey(t => t.UserId);
        });

        modelBuilder.Entity<Location>(location =>
        {
            location.HasIndex(l => new { l.TenantId, l.Name }).IsUnique();
            location.Property(l => l.Name).HasMaxLength(200);
            location.Property(l => l.Timezone).HasMaxLength(64);
            location.Property(l => l.Address).HasMaxLength(500);
        });

        modelBuilder.Entity<Bookings.Booking>(booking =>
        {
            booking.HasIndex(b => new { b.TenantId, b.RoomId, b.StartAt });
            booking.HasIndex(b => new { b.TenantId, b.OrganizerId, b.StartAt });
            booking.Property(b => b.Title).HasMaxLength(200);
            booking.Property(b => b.CancelReason).HasMaxLength(500);
            booking.Property(b => b.SetupNotes).HasMaxLength(1000);
            booking.HasOne(b => b.Room).WithMany().HasForeignKey(b => b.RoomId);
            booking.HasOne(b => b.Organizer).WithMany().HasForeignKey(b => b.OrganizerId);
        });

        modelBuilder.Entity<Room>(room =>
        {
            room.HasIndex(r => new { r.TenantId, r.LocationId });
            room.Property(r => r.Name).HasMaxLength(200);
            room.Property(r => r.Floor).HasMaxLength(100);
            room.HasOne(r => r.Location).WithMany().HasForeignKey(r => r.LocationId);
        });

        // Tenant isolation layer 1 (technical design §4): every ITenantOwned entity is
        // filtered to the bound tenant. Layer 2 (RLS) lives in the migrations.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.IsAssignableTo(typeof(ITenantOwned)))
            {
                typeof(RoomyDbContext)
                    .GetMethod(nameof(ApplyTenantFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
        => modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
            {
                entry.Entity.TenantId = _tenantContext.TenantId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Entity.TenantId != _tenantContext.TenantId)
            {
                throw new InvalidOperationException(
                    $"Cross-tenant write rejected for {entry.Metadata.ClrType.Name}.");
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

}
