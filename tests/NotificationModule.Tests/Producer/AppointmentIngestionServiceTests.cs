using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class AppointmentIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_creates_two_pending_reminders_when_appointment_is_far_in_future()
    {
        var (service, dbFactory) = CreateService();
        var start = DateTimeOffset.UtcNow.AddDays(2);

        var result = await service.IngestAsync(CreateMessage(start), "default", CancellationToken.None);

        Assert.Equal(2, result.PendingNotificationCount);
        Assert.True(result.Created);

        await using var db = await dbFactory.CreateDbContextAsync();
        var reminders = db.ScheduledNotifications
            .Select(x => x.ReminderType)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(["1h", "24h"], reminders);
    }

    [Fact]
    public async Task IngestAsync_stores_null_location_and_instructions_for_null_or_whitespace()
    {
        var (service, dbFactory) = CreateService();
        var message = CreateMessage(DateTimeOffset.UtcNow.AddDays(2)) with
        {
            Location = null!,
            Instructions = "   ",
        };

        await service.IngestAsync(message, "default", CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var appointment = db.Appointments.Single();

        Assert.Null(appointment.Location);
        Assert.Null(appointment.Instructions);
    }

    [Fact]
    public async Task IngestAsync_creates_only_one_hour_reminder_when_appointment_is_90_minutes_away()
    {
        var (service, dbFactory) = CreateService();
        var start = DateTimeOffset.UtcNow.AddMinutes(90);

        var result = await service.IngestAsync(CreateMessage(start.UtcDateTime), "default", CancellationToken.None);

        Assert.Equal(1, result.PendingNotificationCount);

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.Single(db.ScheduledNotifications);
        Assert.Equal("1h", db.ScheduledNotifications.Single().ReminderType);
    }

    [Fact]
    public async Task IngestAsync_creates_no_pending_reminders_when_appointment_already_started()
    {
        var (service, _) = CreateService();
        var start = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = await service.IngestAsync(CreateMessage(start.UtcDateTime), "default", CancellationToken.None);

        Assert.Equal(0, result.PendingNotificationCount);
    }

    [Fact]
    public async Task IngestAsync_cancels_pending_reminders_when_status_is_cancelled()
    {
        var (service, dbFactory) = CreateService();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var message = CreateMessage(start.UtcDateTime);

        await service.IngestAsync(message, "default", CancellationToken.None);

        var cancelled = message with { Status = "Cancelled" };
        var result = await service.IngestAsync(cancelled, "default", CancellationToken.None);

        Assert.Equal(0, result.PendingNotificationCount);

        await using var db = await dbFactory.CreateDbContextAsync();
        Assert.All(
            db.ScheduledNotifications,
            sn => Assert.Equal(ScheduledNotificationStatuses.Cancelled, sn.Status));
    }

    private static (AppointmentIngestionService Service, IDbContextFactory<NotificationDbContext> Factory) CreateService()
    {
        var dbFactory = TestDb.CreateNotificationFactory();
        var service = new AppointmentIngestionService(
            dbFactory,
            TestDb.CreateConfiguration(),
            NullLogger<AppointmentIngestionService>.Instance);
        return (service, dbFactory);
    }

    private static AppointmentMessage CreateMessage(DateTimeOffset start) =>
        CreateMessage(start.UtcDateTime);

    private static AppointmentMessage CreateMessage(DateTime start) => new()
    {
        AppointmentUuid = Guid.NewGuid().ToString("N"),
        OrganizationKey = "default",
        PatientUuid = "patient-1",
        PatientName = "Test Patient",
        PatientPhone = "+31600000000",
        PatientEmail = "test@example.com",
        StartDateTime = start,
        Status = "Confirmed",
        Location = "Room 1",
        Instructions = "Bring ID",
    };
}
