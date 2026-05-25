using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

/// <summary>
/// PII and billing retention deletes (invoked by <see cref="DataRetentionWorker"/>).
/// </summary>
public static class DataRetentionPurge
{
    public static async Task<int> PurgeExpiredPiiAsync(
        NotificationDbContext db,
        DateTimeOffset activityCutoff,
        CancellationToken cancellationToken = default)
    {
        var purgedAt = DateTimeOffset.UtcNow;

        var appointments = await db.Appointments
            .Include(x => x.Deliveries)
            .Where(x => x.PiiPurgedAt == null
                        && x.UpdatedAt < activityCutoff
                        && !x.Deliveries.Any(d => d.SentAt != null && d.SentAt >= activityCutoff))
            .ToListAsync(cancellationToken);

        foreach (var a in appointments)
        {
            a.PatientName = null;
            a.PatientPhone = null;
            a.PatientEmail = null;
            a.Instructions = null;
            a.Location = null;
            a.RawSourcePayload = null;
            a.PiiPurgedAt = purgedAt;
            a.UpdatedAt = purgedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return appointments.Count;
    }

    public static async Task<int> PurgeBillingEventsOlderThanAsync(
        NotificationDbContext db,
        DateTimeOffset occurredBeforeCutoff,
        CancellationToken cancellationToken = default)
    {
        var events = await db.BillingDeliveryEvents
            .Where(x => x.OccurredAt < occurredBeforeCutoff)
            .ToListAsync(cancellationToken);

        db.BillingDeliveryEvents.RemoveRange(events);
        await db.SaveChangesAsync(cancellationToken);
        return events.Count;
    }
}
