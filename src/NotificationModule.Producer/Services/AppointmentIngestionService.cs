using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class AppointmentIngestionService
{
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
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.AppointmentUuid))
            throw new ArgumentException("AppointmentUuid is required.", nameof(message));

        if (message.StartDateTime == default)
            throw new ArgumentException("StartDateTime is required.", nameof(message));

        var resolvedOrganizationKey = ResolveOrganizationKey(organizationKey, message);
        var now = DateTimeOffset.UtcNow;
        var startDateTime = NormalizeToUtc(message.StartDateTime);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await EnsureOrganizationAsync(db, resolvedOrganizationKey, now, cancellationToken);

        var appointment = await db.Appointments
            .Include(x => x.ScheduledNotifications)
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
            CancelPendingNotifications(appointment, now);
            scheduledReminders = [];
        }
        else
        {
            scheduledReminders = RebuildPendingNotifications(
                appointment,
                organization.Id,
                startDateTime,
                now);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved appointment {AppointmentUuid} for organization {OrganizationKey}. Created: {Created}",
            appointment.AppointmentUuid,
            organization.Key,
            created);

        var pendingCount = appointment.ScheduledNotifications.Count(x => x.Status == PendingStatus);

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

    private static IReadOnlyList<ScheduledReminderPlan> RebuildPendingNotifications(
        AppointmentRecord appointment,
        Guid organizationId,
        DateTimeOffset startDateTime,
        DateTimeOffset now)
    {
        CancelPendingNotifications(appointment, now);

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

            appointment.ScheduledNotifications.Add(new ScheduledNotificationRecord
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

    private static void CancelPendingNotifications(AppointmentRecord appointment, DateTimeOffset now)
    {
        foreach (var scheduledNotification in appointment.ScheduledNotifications
            .Where(x => x.Status == PendingStatus))
        {
            scheduledNotification.Status = CancelledStatus;
            scheduledNotification.UpdatedAt = now;
        }
    }

    private static bool IsCancelled(string status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset NormalizeToUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return new DateTimeOffset(utc);
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
