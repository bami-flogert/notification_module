using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class NotificationSchedulerReadinessTests
{
    [Fact]
    public async Task ProcessClaimedBatchAsync_leaves_notification_pending_when_preferred_provider_secrets_missing()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var scheduledNotificationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new OrganizationRecord
            {
                Id = orgId,
                Key = "no-secrets-org",
                Name = "No Secrets Org",
                TimeZone = "UTC",
                PreferredProvider = "SwiftSend",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.Appointments.Add(new AppointmentRecord
            {
                Id = appointmentId,
                OrganizationId = orgId,
                AppointmentUuid = "appt-1",
                PatientUuid = "patient-1",
                StartDateTime = now.AddHours(1),
                Status = "booked",
                SourceSystem = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.ScheduledNotifications.Add(new ScheduledNotificationRecord
            {
                Id = scheduledNotificationId,
                OrganizationId = orgId,
                AppointmentId = appointmentId,
                ReminderType = "24h",
                ScheduledSendAt = now.AddMinutes(-1),
                Status = ScheduledNotificationStatuses.Publishing,
                CreatedAt = now,
                UpdatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        var publisher = new Mock<INotificationMessagePublisher>(MockBehavior.Strict);
        var policyService = new OrganizationProviderPolicyService(factory);

        await using var publishDb = await factory.CreateDbContextAsync();
        var dueNotifications = await publishDb.ScheduledNotifications
            .Include(x => x.Appointment)
            .Include(x => x.Organization)
            .ToListAsync();

        var publishedCount = await NotificationSchedulerPublish.ProcessClaimedBatchAsync(
            publishDb,
            dueNotifications,
            publisher.Object,
            policyService,
            NullLogger.Instance,
            now,
            CancellationToken.None);

        Assert.Equal(0, publishedCount);
        publisher.Verify(x => x.Publish(It.IsAny<AppointmentMessage>()), Times.Never);

        var scheduled = await publishDb.ScheduledNotifications.SingleAsync();
        Assert.Equal(ScheduledNotificationStatuses.Pending, scheduled.Status);
    }

    [Fact]
    public async Task ProcessClaimedBatchAsync_publishes_when_preferred_provider_secrets_exist()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var scheduledNotificationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new OrganizationRecord
            {
                Id = orgId,
                Key = "secrets-org",
                Name = "Secrets Org",
                TimeZone = "UTC",
                PreferredProvider = "SwiftSend",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.ProviderSecrets.Add(new ProviderSecretRecord
            {
                OrganizationId = orgId,
                Provider = "SwiftSend",
                EncryptedPayload = [1],
                Nonce = new byte[12],
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.Appointments.Add(new AppointmentRecord
            {
                Id = appointmentId,
                OrganizationId = orgId,
                AppointmentUuid = "appt-2",
                PatientUuid = "patient-2",
                StartDateTime = now.AddHours(1),
                Status = "booked",
                SourceSystem = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.ScheduledNotifications.Add(new ScheduledNotificationRecord
            {
                Id = scheduledNotificationId,
                OrganizationId = orgId,
                AppointmentId = appointmentId,
                ReminderType = "24h",
                ScheduledSendAt = now.AddMinutes(-1),
                Status = ScheduledNotificationStatuses.Publishing,
                CreatedAt = now,
                UpdatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        var publisher = new Mock<INotificationMessagePublisher>();
        publisher.Setup(x => x.Publish(It.IsAny<AppointmentMessage>()));

        var policyService = new OrganizationProviderPolicyService(factory);

        await using var publishDb = await factory.CreateDbContextAsync();
        var dueNotifications = await publishDb.ScheduledNotifications
            .Include(x => x.Appointment)
            .Include(x => x.Organization)
            .ToListAsync();

        var publishedCount = await NotificationSchedulerPublish.ProcessClaimedBatchAsync(
            publishDb,
            dueNotifications,
            publisher.Object,
            policyService,
            NullLogger.Instance,
            now,
            CancellationToken.None);

        Assert.Equal(1, publishedCount);
        publisher.Verify(x => x.Publish(It.IsAny<AppointmentMessage>()), Times.Once);

        var scheduled = await publishDb.ScheduledNotifications.SingleAsync();
        Assert.Equal(ScheduledNotificationStatuses.Queued, scheduled.Status);
    }
}
