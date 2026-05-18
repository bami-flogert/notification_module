using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class NotificationSchedulerWorker : BackgroundService
{
    private const string PendingStatus = "Pending";
    private const string QueuedStatus = "Queued";

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
        var dueNotifications = await db.ScheduledNotifications
            .Include(x => x.Appointment)
            .Include(x => x.Organization)
            .Where(x => x.Status == PendingStatus && x.ScheduledSendAt <= now)
            .OrderBy(x => x.ScheduledSendAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (dueNotifications.Count == 0)
            return 0;

        var messages = new List<AppointmentMessage>(dueNotifications.Count);
        foreach (var scheduledNotification in dueNotifications)
        {
            var appointment = scheduledNotification.Appointment;
            messages.Add(new AppointmentMessage
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
            });

            scheduledNotification.Status = QueuedStatus;
            scheduledNotification.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        _publisher.PublishBatch(messages);
        return messages.Count;
    }
}
