using Microsoft.EntityFrameworkCore;
using Roomy.Api.Tenants;

namespace Roomy.Api.Common.Persistence;

public sealed class RoomyDbContext(DbContextOptions<RoomyDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO(tenancy): as tenant-owned entities are added, give each a TenantId column,
        // a global query filter on ITenantContext.TenantId, and an RLS policy in its
        // migration (technical design §4). `tenants` itself is a platform table.
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.Property(t => t.Slug).HasMaxLength(40);
            tenant.Property(t => t.Name).HasMaxLength(200);
        });
    }
}
