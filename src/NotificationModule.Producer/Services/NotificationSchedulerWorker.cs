using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class NotificationSchedulerWorker : BackgroundService
{
    private static readonly TimeSpan StalePublishingThreshold = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;
    private readonly RabbitMqPublisher _publisher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationSchedulerWorker> _logger;

    public NotificationSchedulerWorker(
        IDbContextFactory<NotificationDbContext> dbFactory,
        RabbitMqPublisher publisher,
        IConfiguration configuration,
        ILogger<NotificationSchedulerWorker> logger)
    {
        _dbFactory = dbFactory;
        _publisher = publisher;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Max(5, _configuration.GetValue("Scheduler:PollIntervalSeconds", 30)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishDueNotificationsAsync(stoppingToken);
                if (published > 0)
                    _logger.LogInformation("Queued {Count} due scheduled notifications.", published);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler failed while publishing due notifications.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<int> PublishDueNotificationsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batchSize = Math.Clamp(_configuration.GetValue("Scheduler:BatchSize", 25), 1, 100);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await RequeueStalePublishingNotificationsAsync(db, now, cancellationToken);

        var claimedIds = await ClaimDueNotificationIdsAsync(db, now, batchSize, cancellationToken);
        if (claimedIds.Count == 0)
            return 0;

        var dueNotifications = await db.ScheduledNotifications
            .Include(x => x.Appointment)
            .Include(x => x.Organization)
            .Where(x => claimedIds.Contains(x.Id))
            .OrderBy(x => x.ScheduledSendAt)
            .ToListAsync(cancellationToken);

        var publishedCount = 0;
        foreach (var scheduledNotification in dueNotifications)
        {
            var appointment = scheduledNotification.Appointment;
            var message = new AppointmentMessage
            {
                AppointmentUuid = appointment.AppointmentUuid,
                PatientUuid = appointment.PatientUuid,
                PatientName = appointment.PatientName,
                PatientPhone = appointment.PatientPhone,
                PatientEmail = appointment.PatientEmail,
                StartDateTime = appointment.StartDateTime.UtcDateTime,
                Status = appointment.Status,
                OrganizationKey = scheduledNotification.Organization.Key,
                Location = appointment.Location ?? string.Empty,
                Instructions = appointment.Instructions ?? string.Empty,
                ScheduledNotificationId = scheduledNotification.Id,
                ReminderType = scheduledNotification.ReminderType,
            };

            try
            {
                _publisher.Publish(message);
                scheduledNotification.Status = ScheduledNotificationStatuses.Queued;
                scheduledNotification.UpdatedAt = now;
                publishedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
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

    private static async Task RequeueStalePublishingNotificationsAsync(
        NotificationDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var staleBefore = now - StalePublishingThreshold;
        await db.ScheduledNotifications
            .Where(x =>
                x.Status == ScheduledNotificationStatuses.Publishing
                && x.UpdatedAt < staleBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, ScheduledNotificationStatuses.Pending)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);
    }

    private static async Task<List<Guid>> ClaimDueNotificationIdsAsync(
        NotificationDbContext db,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE scheduled_notifications AS sn
            SET "Status" = {ScheduledNotificationStatuses.Publishing},
                "UpdatedAt" = {now}
            FROM (
                SELECT "Id"
                FROM scheduled_notifications
                WHERE "Status" = {ScheduledNotificationStatuses.Pending}
                  AND "ScheduledSendAt" <= {now}
                ORDER BY "ScheduledSendAt"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            ) AS pick
            WHERE sn."Id" = pick."Id"
            RETURNING sn."Id"
            """)
            .ToListAsync(cancellationToken);
    }
}
