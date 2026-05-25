using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;
using System.Diagnostics;

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
            var cycleStart = Stopwatch.GetTimestamp();
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
            finally
            {
                var duration = Stopwatch.GetElapsedTime(cycleStart).TotalMilliseconds;
                NotificationTelemetry.SchedulerCycleDurationMs.Record(duration);
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<int> PublishDueNotificationsAsync(CancellationToken cancellationToken)
    {
        using var activity = NotificationTelemetry.ActivitySource.StartActivity(
            "producer.scheduler.publish_due",
            ActivityKind.Internal);

        var now = DateTimeOffset.UtcNow;
        var batchSize = Math.Clamp(_configuration.GetValue("Scheduler:BatchSize", 25), 1, 100);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await RequeueStalePublishingNotificationsAsync(db, now, cancellationToken);

        // Update pending queue metrics: count and oldest pending age (seconds)
        var pendingStats = await db.ScheduledNotifications
            .Where(x => x.Status == ScheduledNotificationStatuses.Pending && x.ScheduledSendAt <= now)
            .GroupBy(x => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min(x => x.ScheduledSendAt) })
            .FirstOrDefaultAsync(cancellationToken);

        if (pendingStats is null)
        {
            NotificationTelemetry.SetPendingMetrics(0, 0);
        }
        else
        {
            NotificationTelemetry.SetPendingMetrics(
                pendingStats.Count,
                (now - pendingStats.Oldest).TotalSeconds);
        }

        var claimedIds = await ClaimDueNotificationIdsAsync(db, now, batchSize, cancellationToken);
        NotificationTelemetry.SchedulerDueNotificationsCount.Add(claimedIds.Count);
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
                Location = appointment.Location ?? string.Empty,
                Instructions = appointment.Instructions ?? string.Empty,
                ScheduledNotificationId = scheduledNotification.Id,
                ReminderType = scheduledNotification.ReminderType,
                TargetProvider = organization.PreferredProvider,
                TriedProviders = string.Empty,
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

        activity?.SetTag("scheduler.claimed_count", claimedIds.Count);
        activity?.SetTag("scheduler.published_count", publishedCount);
        if (publishedCount > 0)
            NotificationTelemetry.ScheduledNotificationsPublished.Add(publishedCount);

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
