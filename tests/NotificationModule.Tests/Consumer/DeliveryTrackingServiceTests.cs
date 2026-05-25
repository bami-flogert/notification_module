using Microsoft.Extensions.Logging.Abstractions;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Consumer;

public sealed class DeliveryTrackingServiceTests
{
    private static readonly string[] AllProviders = ["AsyncFlow", "LegacyLink", "SecurePost", "SwiftSend"];

    [Fact]
    public async Task RecordAsync_marks_scheduled_notification_sent_when_all_providers_succeed()
    {
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync();
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        foreach (var provider in AllProviders)
            await service.RecordAsync(message, provider, success: true, errorMessage: null, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var scheduled = await db.ScheduledNotifications.FindAsync(scheduledNotificationId);

        Assert.Equal(ScheduledNotificationStatuses.Sent, scheduled!.Status);
        Assert.Equal(4, db.NotificationDeliveries.Count());
    }

    [Fact]
    public async Task RecordAsync_marks_scheduled_notification_failed_when_any_provider_fails()
    {
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync();
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        await service.RecordAsync(message, "SwiftSend", success: true, errorMessage: null, CancellationToken.None);
        await service.RecordAsync(message, "SecurePost", success: false, errorMessage: "boom", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var scheduled = await db.ScheduledNotifications.FindAsync(scheduledNotificationId);

        Assert.Equal(ScheduledNotificationStatuses.Sent, scheduled!.Status);
    }

    [Fact]
    public async Task RecordAsync_leaves_status_unchanged_until_provider_chain_is_exhausted()
    {
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync(
            initialStatus: ScheduledNotificationStatuses.Queued);
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        await service.RecordAsync(message, "SwiftSend", success: false, errorMessage: "boom", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var scheduled = await db.ScheduledNotifications.FindAsync(scheduledNotificationId);

        Assert.Equal(ScheduledNotificationStatuses.Queued, scheduled!.Status);
    }

    [Fact]
    public async Task RecordAsync_writes_billing_event_without_pii()
    {
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync();
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        await service.RecordAsync(message, "SwiftSend", success: true, errorMessage: null, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var billing = db.BillingDeliveryEvents.Single();

        Assert.Equal("SwiftSend", billing.Provider);
        Assert.Equal("1h", billing.ReminderType);
        Assert.Equal("Sent", billing.Status);
        Assert.NotEqual(Guid.Empty, billing.CorrelationId);
        Assert.NotEqual(Guid.Empty, billing.OrganizationId);
        Assert.NotEqual(message.AppointmentUuid, billing.CorrelationId.ToString());

        var billingPropertyNames = billing.GetType().GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("PatientName", billingPropertyNames);
        Assert.DoesNotContain("PatientPhone", billingPropertyNames);
        Assert.DoesNotContain("PatientEmail", billingPropertyNames);
        Assert.DoesNotContain("AppointmentUuid", billingPropertyNames);
        Assert.Contains("ProviderMessageId", billingPropertyNames);
    }

    [Fact]
    public async Task RecordAsync_persists_provider_message_id_on_delivery_and_billing()
    {
        const string providerMessageId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync();
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        await service.RecordAsync(
            message,
            "SwiftSend",
            success: true,
            errorMessage: null,
            CancellationToken.None,
            providerMessageId: providerMessageId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var delivery = db.NotificationDeliveries.Single();
        var billing = db.BillingDeliveryEvents.Single();

        Assert.Equal(providerMessageId, delivery.ProviderMessageId);
        Assert.Equal(providerMessageId, billing.ProviderMessageId);
    }

    [Fact]
    public async Task RecordAsync_marks_scheduled_notification_failed_when_all_chain_providers_fail()
    {
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync(
            initialStatus: ScheduledNotificationStatuses.Queued);
        var service = CreateService(dbFactory);
        var message = CreateMessage(scheduledNotificationId);

        await service.RecordAsync(message, "SwiftSend", success: false, errorMessage: "boom", CancellationToken.None);
        await service.RecordAsync(message, "SecurePost", success: false, errorMessage: "boom", CancellationToken.None);
        await service.RecordAsync(message, "LegacyLink", success: false, errorMessage: "boom", CancellationToken.None);
        await service.RecordAsync(message, "AsyncFlow", success: false, errorMessage: "boom", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var scheduled = await db.ScheduledNotifications.FindAsync(scheduledNotificationId);

        Assert.Equal(ScheduledNotificationStatuses.Failed, scheduled!.Status);
    }

    private static DeliveryTrackingService CreateService(IDbContextFactory<SecretsDbContext> dbFactory) =>
        new(dbFactory, NullLogger<DeliveryTrackingService>.Instance);

    private static async Task<(IDbContextFactory<SecretsDbContext> Factory, Guid ScheduledNotificationId)> SeedScheduledNotificationAsync(
        string initialStatus = ScheduledNotificationStatuses.Publishing)
    {
        var dbFactory = TestDb.CreateSecretsFactory();
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync();
        var organization = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            TimeZone = "UTC",
            IsEnabled = true,
            PreferredProvider = "SwiftSend",
            FallbackProviders = "SecurePost,LegacyLink,AsyncFlow",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var appointment = new AppointmentRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            AppointmentUuid = "appt-1",
            PatientUuid = "patient-1",
            PatientName = "Test",
            PatientPhone = "+31600000000",
            PatientEmail = "test@example.com",
            StartDateTime = now.AddHours(2),
            Status = "Confirmed",
            SourceSystem = "test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var scheduledNotification = new ScheduledNotificationRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            AppointmentId = appointment.Id,
            ReminderType = "1h",
            ScheduledSendAt = now.AddMinutes(-1),
            Status = initialStatus,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);
        db.Appointments.Add(appointment);
        db.ScheduledNotifications.Add(scheduledNotification);
        await db.SaveChangesAsync();

        return (dbFactory, scheduledNotification.Id);
    }

    private static AppointmentMessage CreateMessage(Guid scheduledNotificationId) => new()
    {
        AppointmentUuid = "appt-1",
        OrganizationKey = "default",
        PatientUuid = "patient-1",
        PatientName = "Test",
        PatientPhone = "+31600000000",
        PatientEmail = "test@example.com",
        StartDateTime = DateTime.UtcNow.AddHours(2),
        Status = "Confirmed",
        ScheduledNotificationId = scheduledNotificationId,
        ReminderType = "1h",
    };
}
