using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

internal static class ProviderLogging
{
    public static void LogHttpResult(
        ILogger logger,
        string providerName,
        AppointmentMessage message,
        int httpStatusCode)
    {
        logger.LogInformation(
            "{Provider} HTTP {HttpStatus} for appointment {AppointmentUuid} organization {OrganizationKey} scheduled {ScheduledNotificationId}",
            providerName,
            httpStatusCode,
            message.AppointmentUuid,
            message.OrganizationKey,
            message.ScheduledNotificationId);
    }
}
