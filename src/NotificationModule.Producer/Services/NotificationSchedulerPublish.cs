using Microsoft.Extensions.Logging;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public static class NotificationSchedulerPublish
{
    public static async Task<int> ProcessClaimedBatchAsync(
        NotificationDbContext db,
        IReadOnlyList<ScheduledNotificationRecord> dueNotifications,
        INotificationMessagePublisher publisher,
        OrganizationProviderPolicyService policyService,
        ILogger logger,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var publishedCount = 0;

        foreach (var scheduledNotification in dueNotifications)
        {
            var appointment = scheduledNotification.Appointment;
            var organization = scheduledNotification.Organization;
            var message = new AppointmentMessage
            {
                AppointmentUuid = appointment.AppointmentUuid,
                PatientUuid = appointment.PatientUuid,
                PatientName = appointment.PatientName ?? string.Empty,
                PatientPhone = appointment.PatientPhone ?? string.Empty,
                PatientEmail = appointment.PatientEmail ?? string.Empty,
                StartDateTime = appointment.StartDateTime.UtcDateTime,
                Status = appointment.Status,
                OrganizationKey = organization.Key,
                TimeZone = organization.TimeZone,
                Location = appointment.Location ?? string.Empty,
                Instructions = appointment.Instructions ?? string.Empty,
                ScheduledNotificationId = scheduledNotification.Id,
                ReminderType = scheduledNotification.ReminderType,
                TargetProvider = organization.PreferredProvider,
                TriedProviders = string.Empty,
            };

            if (!await policyService.HasProviderSecretAsync(
                    organization.Id,
                    organization.PreferredProvider,
                    cancellationToken))
            {
                logger.LogWarning(
                    "Skipping publish for scheduled notification {ScheduledNotificationId}; preferred provider {PreferredProvider} has no secrets for organization {OrganizationKey}.",
                    scheduledNotification.Id,
                    organization.PreferredProvider,
                    organization.Key);

                scheduledNotification.Status = ScheduledNotificationStatuses.Pending;
                scheduledNotification.UpdatedAt = now;
                continue;
            }

            try
            {
                publisher.Publish(message);
                scheduledNotification.Status = ScheduledNotificationStatuses.Queued;
                scheduledNotification.UpdatedAt = now;
                publishedCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to publish scheduled notification {ScheduledNotificationId}; reverting to Pending.",
                    scheduledNotification.Id);

                scheduledNotification.Status = ScheduledNotificationStatuses.Pending;
                scheduledNotification.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return publishedCount;
    }
}
