using Microsoft.EntityFrameworkCore;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Consumer.Services;

public sealed class DeliveryTrackingService
{
    private static readonly string[] ProviderNames =
    [
        "SwiftSend",
        "SecurePost",
        "LegacyLink",
        "AsyncFlow",
    ];

    private readonly IDbContextFactory<SecretsDbContext> _dbFactory;
    private readonly ILogger<DeliveryTrackingService> _logger;

    public DeliveryTrackingService(
        IDbContextFactory<SecretsDbContext> dbFactory,
        ILogger<DeliveryTrackingService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(
        AppointmentMessage message,
        string provider,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (message.ScheduledNotificationId is null)
            return;

        var now = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var scheduledNotification = await db.ScheduledNotifications
            .Include(x => x.Appointment)
            .SingleOrDefaultAsync(x => x.Id == message.ScheduledNotificationId.Value, cancellationToken);

        if (scheduledNotification is null)
        {
            _logger.LogWarning(
                "Cannot track delivery for unknown scheduled notification {ScheduledNotificationId}.",
                message.ScheduledNotificationId);
            return;
        }

        var delivery = await db.NotificationDeliveries
            .SingleOrDefaultAsync(
                x => x.ScheduledNotificationId == scheduledNotification.Id && x.Provider == provider,
                cancellationToken);

        if (delivery is null)
        {
            delivery = new NotificationDeliveryRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = scheduledNotification.OrganizationId,
                AppointmentId = scheduledNotification.AppointmentId,
                ScheduledNotificationId = scheduledNotification.Id,
                Provider = provider,
                CreatedAt = now,
            };
            db.NotificationDeliveries.Add(delivery);
        }

        delivery.Status = success ? "Sent" : "Failed";
        delivery.SentAt = success ? now : null;
        delivery.FailedAt = success ? null : now;
        delivery.ErrorMessage = success ? null : Truncate(errorMessage, 2000);
        delivery.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await UpdateScheduledNotificationStatusAsync(db, scheduledNotification.Id, now, cancellationToken);
    }

    private static async Task UpdateScheduledNotificationStatusAsync(
        SecretsDbContext db,
        Guid scheduledNotificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var statuses = await db.NotificationDeliveries
            .Where(x => x.ScheduledNotificationId == scheduledNotificationId)
            .Select(x => x.Status)
            .ToListAsync(cancellationToken);

        var scheduledNotification = await db.ScheduledNotifications
            .SingleAsync(x => x.Id == scheduledNotificationId, cancellationToken);

        if (statuses.Count >= ProviderNames.Length && statuses.All(x => x == "Sent"))
        {
            scheduledNotification.Status = "Sent";
            scheduledNotification.UpdatedAt = now;
        }
        else if (statuses.Any(x => x == "Failed"))
        {
            scheduledNotification.Status = "Failed";
            scheduledNotification.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
