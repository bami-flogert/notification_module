using System.Collections.Concurrent;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class AppointmentIngestionService
{
    private static readonly ConcurrentDictionary<string, byte> TimeZoneWarningsLogged = new(StringComparer.OrdinalIgnoreCase);

    private const string PendingStatus = ScheduledNotificationStatuses.Pending;
    private const string CancelledStatus = ScheduledNotificationStatuses.Cancelled;

    private static readonly ReminderDefinition[] ReminderDefinitions =
    [
        new("24h", TimeSpan.FromHours(24)),
        new("1h", TimeSpan.FromHours(1)),
    ];

    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppointmentIngestionService> _logger;

    public AppointmentIngestionService(
        IDbContextFactory<NotificationDbContext> dbFactory,
        IConfiguration configuration,
        ILogger<AppointmentIngestionService> logger)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AppointmentIngestionResult> IngestAsync(
        AppointmentMessage message,
        string? organizationKey,
        CancellationToken cancellationToken)
    {
        using var activity = NotificationTelemetry.ActivitySource.StartActivity(
            "producer.appointment.ingest",
            ActivityKind.Internal);

        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.AppointmentUuid))
            throw new ArgumentException("AppointmentUuid is required.", nameof(message));

        if (message.StartDateTime == default)
            throw new ArgumentException("StartDateTime is required.", nameof(message));

        var resolvedOrganizationKey = ResolveOrganizationKey(organizationKey, message);
        var now = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await EnsureOrganizationAsync(db, resolvedOrganizationKey, now, cancellationToken);
        var startDateTime = NormalizeToUtc(message.StartDateTime, organization.TimeZone, organization.Key);

        var appointment = await db.Appointments
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organization.Id && x.AppointmentUuid == message.AppointmentUuid,
                cancellationToken);

        var created = appointment is null;
        if (created && startDateTime <= now)
            throw new ArgumentException("StartDateTime must be in the future for new appointments.", nameof(message));

        if (appointment is null)
        {
            appointment = new AppointmentRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                AppointmentUuid = message.AppointmentUuid,
                CreatedAt = now,
            };
            db.Appointments.Add(appointment);
        }

        appointment.PatientUuid = message.PatientUuid;
        appointment.PatientName = message.PatientName;
        appointment.PatientPhone = message.PatientPhone;
        appointment.PatientEmail = message.PatientEmail;
        appointment.StartDateTime = startDateTime;
        appointment.Status = message.Status;
        appointment.Location = EmptyToNull(message.Location);
        appointment.Instructions = EmptyToNull(message.Instructions);
        appointment.SourceSystem = "OpenMRS";
        appointment.RawSourcePayload = JsonSerializer.Serialize(message);
        appointment.UpdatedAt = now;

        IReadOnlyList<ScheduledReminderPlan> scheduledReminders;
        if (IsCancelled(message.Status))
        {
            await CancelPendingNotificationsInDatabaseAsync(db, appointment.Id, now, cancellationToken);
            scheduledReminders = [];
        }
        else
        {
            scheduledReminders = await RebuildPendingNotificationsAsync(
                db,
                appointment,
                organization.Id,
                startDateTime,
                now,
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved appointment {AppointmentUuid} for organization {OrganizationKey}. Created: {Created}",
            appointment.AppointmentUuid,
            organization.Key,
            created);

        var pendingCount = await db.ScheduledNotifications.CountAsync(
            x => x.AppointmentId == appointment.Id && x.Status == PendingStatus,
            cancellationToken);
        var createdNotificationCount = scheduledReminders.Count;

        activity?.SetTag("organization.key", organization.Key);
        activity?.SetTag("appointment.uuid", appointment.AppointmentUuid);
        activity?.SetTag("appointment.created", created);
        activity?.SetTag("scheduled.notifications.created", createdNotificationCount);

        NotificationTelemetry.AppointmentsIngested.Add(
            1,
            new KeyValuePair<string, object?>("organization.key", organization.Key),
            new KeyValuePair<string, object?>("appointment.created", created));

        if (createdNotificationCount > 0)
        {
            NotificationTelemetry.ScheduledNotificationsCreated.Add(
                createdNotificationCount,
                new KeyValuePair<string, object?>("organization.key", organization.Key));
        }

        return new AppointmentIngestionResult(
            organization.Key,
            appointment.AppointmentUuid,
            created,
            pendingCount,
            scheduledReminders);
    }

    private string ResolveOrganizationKey(string? organizationKey, AppointmentMessage message)
    {
        if (!string.IsNullOrWhiteSpace(organizationKey))
            return organizationKey.Trim();

        if (!string.IsNullOrWhiteSpace(message.OrganizationKey))
            return message.OrganizationKey.Trim();

        return _configuration["Organizations:Default:Key"] ?? "default";
    }

    private async Task<OrganizationRecord> EnsureOrganizationAsync(
        NotificationDbContext db,
        string key,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (organization is not null)
        {
            if (!organization.IsEnabled)
                throw new InvalidOperationException($"Organization '{key}' is disabled.");

            return organization;
        }

        organization = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = key,
            TimeZone = _configuration["Organizations:Default:TimeZone"] ?? "UTC",
            PreferredProvider = _configuration["Organizations:Default:PreferredProvider"] ?? "SwiftSend",
            FallbackProviders = EmptyToNull(_configuration["Organizations:Default:FallbackProviders"]),
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private async Task<IReadOnlyList<ScheduledReminderPlan>> RebuildPendingNotificationsAsync(
        NotificationDbContext db,
        AppointmentRecord appointment,
        Guid organizationId,
        DateTimeOffset startDateTime,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await CancelPendingNotificationsInDatabaseAsync(db, appointment.Id, now, cancellationToken);

        if (startDateTime <= now)
            return [];

        var plans = new List<ScheduledReminderPlan>();
        var timeUntilStart = startDateTime - now;

        foreach (var definition in ReminderDefinitions)
        {
            // Less than 1h until the appointment: only the 1h reminder applies.
            if (definition.ReminderType == "24h" && timeUntilStart < TimeSpan.FromHours(1))
                continue;

            var idealSendAt = startDateTime.Subtract(definition.OffsetBeforeAppointment);
            var isCatchUp = idealSendAt <= now;
            var scheduledSendAt = isCatchUp ? now : idealSendAt;

            db.ScheduledNotifications.Add(new ScheduledNotificationRecord
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppointmentId = appointment.Id,
                ReminderType = definition.ReminderType,
                ScheduledSendAt = scheduledSendAt,
                Status = PendingStatus,
                CreatedAt = now,
                UpdatedAt = now,
            });

            plans.Add(new ScheduledReminderPlan(
                definition.ReminderType,
                scheduledSendAt,
                isCatchUp));
        }

        return plans;
    }

    private static async Task CancelPendingNotificationsInDatabaseAsync(
        NotificationDbContext db,
        Guid appointmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.ScheduledNotifications
                .Where(x => x.AppointmentId == appointmentId && x.Status == PendingStatus)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, CancelledStatus)
                        .SetProperty(x => x.UpdatedAt, now),
                    cancellationToken);
            return;
        }

        var pending = await db.ScheduledNotifications
            .Where(x => x.AppointmentId == appointmentId && x.Status == PendingStatus)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            notification.Status = CancelledStatus;
            notification.UpdatedAt = now;
        }
    }

    private static bool IsCancelled(string status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private DateTimeOffset NormalizeToUtc(DateTime value, string? timeZone, string organizationKey)
    {
        if (value.Kind != DateTimeKind.Unspecified)
            return new DateTimeOffset(value.ToUniversalTime());

        if (string.IsNullOrWhiteSpace(timeZone))
        {
            WarnTimeZoneFallbackOnce(organizationKey, "missing or empty");
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }

        if (OrganizationTimeZone.TryGetTimeZoneInfo(timeZone, out var tz))
            return OrganizationTimeZone.ConvertUnspecifiedLocalToUtc(value, tz);

        WarnTimeZoneFallbackOnce(organizationKey, $"invalid or unknown timezone '{timeZone}'");
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private void WarnTimeZoneFallbackOnce(string organizationKey, string reason)
    {
        if (!TimeZoneWarningsLogged.TryAdd(organizationKey, 0))
            return;

        _logger.LogWarning(
            "Organization '{OrganizationKey}' timezone is {Reason}; falling back to UTC for appointment start time.",
            organizationKey,
            reason);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ReminderDefinition(string ReminderType, TimeSpan OffsetBeforeAppointment);
}

public sealed record AppointmentIngestionResult(
    string OrganizationKey,
    string AppointmentUuid,
    bool Created,
    int PendingNotificationCount,
    IReadOnlyList<ScheduledReminderPlan> ScheduledReminders);
