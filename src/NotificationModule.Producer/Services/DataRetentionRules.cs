using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public static class DataRetentionRules
{
    /// <summary>
    /// PII may be purged when last activity (UpdatedAt or latest delivery SentAt) is older than cutoff.
    /// </summary>
    public static bool IsDueForPiiPurge(AppointmentRecord appointment, DateTimeOffset cutoff)
    {
        if (appointment.PiiPurgedAt is not null)
            return false;

        if (appointment.UpdatedAt >= cutoff)
            return false;

        return !appointment.Deliveries.Any(d => d.SentAt is not null && d.SentAt >= cutoff);
    }
}
