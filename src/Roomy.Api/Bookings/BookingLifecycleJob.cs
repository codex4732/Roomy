using Microsoft.EntityFrameworkCore;
using Quartz;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Tenants;

namespace Roomy.Api.Bookings;

/// <summary>
/// Runs every minute (technical design §6.4): auto-release no-shows (FR-6.3), expire
/// stale approval requests (FR-5.2), and complete finished bookings. Iterates tenants
/// and binds each in its own scope so RLS sees the right tenant.
/// </summary>
[DisallowConcurrentExecution]
public sealed class BookingLifecycleJob(IServiceProvider services, ILogger<BookingLifecycleJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        List<(Guid Id, TenantSettings Settings)> tenants;
        using (var scope = services.CreateScope())
        {
            tenants = (await scope.ServiceProvider.GetRequiredService<RoomyDbContext>()
                .Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active)
                .Select(t => new { t.Id, t.Settings })
                .ToListAsync())
                .Select(t => (t.Id, t.Settings))
                .ToList();
        }

        foreach (var (tenantId, settings) in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenantId, settings);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Booking lifecycle failed for tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, TenantSettings settings)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Bind(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RoomyDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Auto-release (FR-6.3): confirmed, check-in required, grace elapsed, never checked in.
        var graceCutoff = now.AddMinutes(-settings.CheckinGraceMinutes);
        var noShows = await db.Bookings.Include(b => b.Organizer)
            .Where(b => b.Status == BookingStatus.Confirmed && b.Room!.CheckinRequired
                && b.CheckedInAt == null && b.StartAt < graceCutoff)
            .ToListAsync();
        foreach (var booking in noShows)
        {
            booking.Status = BookingStatus.AutoReleased;
            booking.Organizer!.NoShowCount++;
        }

        // Approval expiry (FR-5.2): min(expiry hours after submission, booking start).
        var submittedCutoff = now.AddHours(-settings.ApprovalExpiryHours);
        await db.Bookings
            .Where(b => b.Status == BookingStatus.PendingApproval
                && (b.CreatedAt < submittedCutoff || b.StartAt < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.Declined)
                .SetProperty(b => b.CancelReason, "Auto-declined: request expired."));

        // Completion sweep: checked-in meetings, and confirmed ones on rooms without check-in.
        await db.Bookings
            .Where(b => b.EndAt <= now
                && (b.Status == BookingStatus.CheckedIn
                    || (b.Status == BookingStatus.Confirmed && !b.Room!.CheckinRequired)))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Completed));

        if (noShows.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Auto-released {Count} no-show booking(s) for tenant {TenantId}",
                noShows.Count, tenantId);
        }
    }
}
