using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class DataRetentionRulesTests
{
    private static readonly DateTimeOffset Cutoff = DateTimeOffset.UtcNow.AddDays(-14);

    [Fact]
    public void IsDueForPiiPurge_returns_false_when_UpdatedAt_is_recent()
    {
        var appointment = CreateAppointment(
            updatedAt: DateTimeOffset.UtcNow.AddDays(-3),
            sentAt: DateTimeOffset.UtcNow.AddDays(-20));

        Assert.False(DataRetentionRules.IsDueForPiiPurge(appointment, Cutoff));
    }

    [Fact]
    public void IsDueForPiiPurge_returns_false_when_recent_SentAt_blocks_purge()
    {
        var appointment = CreateAppointment(
            updatedAt: DateTimeOffset.UtcNow.AddDays(-20),
            sentAt: DateTimeOffset.UtcNow.AddDays(-3));

        Assert.False(DataRetentionRules.IsDueForPiiPurge(appointment, Cutoff));
    }

    [Fact]
    public void IsDueForPiiPurge_returns_true_when_last_activity_is_old()
    {
        var appointment = CreateAppointment(
            updatedAt: DateTimeOffset.UtcNow.AddDays(-20),
            sentAt: null);

        Assert.True(DataRetentionRules.IsDueForPiiPurge(appointment, Cutoff));
    }

    [Fact]
    public void IsDueForPiiPurge_returns_false_when_already_purged()
    {
        var appointment = CreateAppointment(
            updatedAt: DateTimeOffset.UtcNow.AddDays(-20),
            sentAt: null);
        appointment.PiiPurgedAt = DateTimeOffset.UtcNow;

        Assert.False(DataRetentionRules.IsDueForPiiPurge(appointment, Cutoff));
    }

    private static AppointmentRecord CreateAppointment(
        DateTimeOffset updatedAt,
        DateTimeOffset? sentAt)
    {
        var appointment = new AppointmentRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            AppointmentUuid = "appt-1",
            PatientUuid = "patient-1",
            StartDateTime = updatedAt,
            Status = "Confirmed",
            SourceSystem = "test",
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt,
        };

        if (sentAt is not null)
        {
            appointment.Deliveries.Add(new NotificationDeliveryRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = appointment.OrganizationId,
                AppointmentId = appointment.Id,
                ScheduledNotificationId = Guid.NewGuid(),
                Provider = "SwiftSend",
                Status = "Sent",
                SentAt = sentAt,
                CreatedAt = sentAt.Value,
                UpdatedAt = sentAt.Value,
            });
        }

        return appointment;
    }
}
