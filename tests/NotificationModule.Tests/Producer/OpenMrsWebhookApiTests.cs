using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class OpenMrsWebhookApiTests : IClassFixture<ProducerWebApplicationFactory>
{
    private readonly ProducerWebApplicationFactory _factory;

    public OpenMrsWebhookApiTests(ProducerWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_openmrs_webhook_creates_pending_reminders()
    {
        var client = _factory.CreateAuthenticatedClient();
        var start = DateTime.UtcNow.AddDays(3);

        var payload = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            AppointmentUuid = Guid.NewGuid().ToString(),
            Status = "Scheduled",
            StartDateTime = start,
            EndDateTime = start.AddMinutes(30),
            PatientUuid = Guid.NewGuid().ToString(),
            PatientName = "John Doe",
            PatientPhone = "+31600000000",
            Service = "General Medicine",
            Location = "Outpatient",
            Comments = "Test",
        };

        var response = await client.PostAsJsonAsync(
            "/api/webhooks/openmrs/appointments/default",
            payload);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("pendingNotifications").GetInt32());
    }

    [Fact]
    public async Task Post_openmrs_webhook_duplicate_created_does_not_duplicate_reminders()
    {
        var client = _factory.CreateAuthenticatedClient();
        var uuid = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow.AddDays(3);

        var payload = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            AppointmentUuid = uuid,
            Status = "Scheduled",
            StartDateTime = start,
            PatientUuid = "p",
            PatientName = "John",
            PatientPhone = "+31600000000",
        };

        var first = await client.PostAsJsonAsync("/api/webhooks/openmrs/appointments/default", payload);
        var second = await client.PostAsJsonAsync("/api/webhooks/openmrs/appointments/default", payload);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var appointment = await db.Appointments.SingleAsync(a => a.AppointmentUuid == uuid);
        var pending = await db.ScheduledNotifications
            .Where(sn => sn.AppointmentId == appointment.Id && sn.Status == ScheduledNotificationStatuses.Pending)
            .ToListAsync();

        Assert.Equal(2, pending.Count);
        Assert.Equal(["1h", "24h"], pending.Select(p => p.ReminderType).OrderBy(x => x));
    }

    [Fact]
    {
        var client = _factory.CreateAuthenticatedClient();
        var uuid = Guid.NewGuid().ToString();
        var start = DateTime.UtcNow.AddDays(3);

        var create = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            AppointmentUuid = uuid,
            Status = "Scheduled",
            StartDateTime = start,
            PatientUuid = "p",
            PatientName = "John",
            PatientPhone = "+31600000000",
        };

        await client.PostAsJsonAsync("/api/webhooks/openmrs/appointments/default", create);

        var cancel = create with { Event = "CANCELLED" };
        var response = await client.PostAsJsonAsync("/api/webhooks/openmrs/appointments/default", cancel);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var appointment = await db.Appointments.SingleAsync(a => a.AppointmentUuid == uuid);
        var pending = await db.ScheduledNotifications
            .CountAsync(sn => sn.AppointmentId == appointment.Id && sn.Status == ScheduledNotificationStatuses.Pending);

        Assert.Equal(0, pending);
    }

    [Fact]
    public async Task Post_openmrs_webhook_requires_api_key()
    {
        var client = _factory.CreateClient();
        var payload = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            AppointmentUuid = "x",
            StartDateTime = DateTime.UtcNow.AddDays(1),
        };

        var response = await client.PostAsJsonAsync("/api/webhooks/openmrs/appointments/default", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
