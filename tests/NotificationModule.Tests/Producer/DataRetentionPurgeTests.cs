using Microsoft.EntityFrameworkCore;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class DataRetentionPurgeTests
{
    [Fact]
    public async Task PurgeExpiredPiiAsync_clears_pii_fields_and_sets_PiiPurgedAt()
    {
        var factory = TestDb.CreateNotificationFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var oldActivity = cutoff.AddDays(-1);

        await SeedAppointmentAsync(factory, oldActivity, patientName: "Jane Doe");

        await using var db = await factory.CreateDbContextAsync();
        var purged = await DataRetentionPurge.PurgeExpiredPiiAsync(db, cutoff, CancellationToken.None);

        Assert.Equal(1, purged);
        var appointment = await db.Appointments.SingleAsync();
        Assert.Null(appointment.PatientName);
        Assert.Null(appointment.PatientPhone);
        Assert.Null(appointment.PatientEmail);
        Assert.Null(appointment.Instructions);
        Assert.Null(appointment.Location);
        Assert.Null(appointment.RawSourcePayload);
        Assert.NotNull(appointment.PiiPurgedAt);
        Assert.Equal("patient-uuid-1", appointment.PatientUuid);
        Assert.Equal("appt-uuid-1", appointment.AppointmentUuid);
    }

    [Fact]
    public async Task PurgeExpiredPiiAsync_skips_when_recent_delivery_sent_at_blocks_purge()
    {
        var factory = TestDb.CreateNotificationFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        var oldUpdated = cutoff.AddDays(-5);
        var recentSent = cutoff.AddDays(1);

        await SeedAppointmentAsync(
            factory,
            oldUpdated,
            patientName: "Jane Doe",
            recentDeliverySentAt: recentSent);

        await using var db = await factory.CreateDbContextAsync();
        var purged = await DataRetentionPurge.PurgeExpiredPiiAsync(db, cutoff, CancellationToken.None);

        Assert.Equal(0, purged);
        var appointment = await db.Appointments.SingleAsync();
        Assert.Equal("Jane Doe", appointment.PatientName);
        Assert.Null(appointment.PiiPurgedAt);
    }

    [Fact]
    public async Task PurgeBillingEventsOlderThanAsync_removes_only_stale_rows()
    {
        var factory = TestDb.CreateNotificationFactory();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-365);
        var orgId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BillingDeliveryEvents.AddRange(
                new BillingDeliveryEventRecord
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = orgId,
                    Provider = "SwiftSend",
                    ReminderType = "24h",
                    Status = "Sent",
                    OccurredAt = cutoff.AddDays(-10),
                    CorrelationId = Guid.NewGuid(),
                },
                new BillingDeliveryEventRecord
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = orgId,
                    Provider = "SwiftSend",
                    ReminderType = "1h",
                    Status = "Sent",
                    OccurredAt = cutoff.AddDays(1),
                    CorrelationId = Guid.NewGuid(),
                });
            await db.SaveChangesAsync();
        }

        await using var purgeDb = await factory.CreateDbContextAsync();
        var deleted = await DataRetentionPurge.PurgeBillingEventsOlderThanAsync(
            purgeDb,
            cutoff,
            CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Single(await purgeDb.BillingDeliveryEvents.ToListAsync());
        Assert.Equal("1h", (await purgeDb.BillingDeliveryEvents.SingleAsync()).ReminderType);
    }

    private static async Task SeedAppointmentAsync(
        IDbContextFactory<NotificationDbContext> factory,
        DateTimeOffset updatedAt,
        string patientName,
        DateTimeOffset? recentDeliverySentAt = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var orgId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        db.Organizations.Add(new OrganizationRecord
        {
            Id = orgId,
            Key = "test-org",
            Name = "Test Org",
            TimeZone = "UTC",
            PreferredProvider = "SwiftSend",
            IsEnabled = true,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        });

        var appointment = new AppointmentRecord
        {
            Id = appointmentId,
            OrganizationId = orgId,
            AppointmentUuid = "appt-uuid-1",
            PatientUuid = "patient-uuid-1",
            PatientName = patientName,
            PatientPhone = "+31600000000",
            PatientEmail = "test@example.com",
            Instructions = "Bring ID",
            Location = "Room 1",
            RawSourcePayload = """{"PatientName":"Jane Doe"}""",
            StartDateTime = updatedAt.AddDays(1),
            Status = "Confirmed",
            SourceSystem = "test",
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt,
        };

        if (recentDeliverySentAt is not null)
        {
            appointment.Deliveries.Add(new NotificationDeliveryRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                AppointmentId = appointmentId,
                ScheduledNotificationId = Guid.NewGuid(),
                Provider = "SwiftSend",
                Status = "Sent",
                SentAt = recentDeliverySentAt,
                CreatedAt = recentDeliverySentAt.Value,
                UpdatedAt = recentDeliverySentAt.Value,
            });
        }

        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
    }
}
