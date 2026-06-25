using NotificationModule.Producer.OpenMrs;
using NotificationModule.Shared.Models;

namespace NotificationModule.Tests.Producer;

public sealed class OpenMrsWebhookMapperTests
{
    private readonly OpenMrsWebhookMapper _mapper = new();

    [Fact]
    public void ToAppointmentMessage_maps_created_payload()
    {
        var webhook = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            AppointmentUuid = "appt-1",
            Status = "Scheduled",
            StartDateTime = DateTime.UtcNow.AddDays(2),
            EndDateTime = DateTime.UtcNow.AddDays(2).AddMinutes(30),
            PatientUuid = "patient-1",
            PatientName = "John Doe",
            PatientPhone = "+31612345678",
            PatientEmail = "john@example.com",
            Service = "General Medicine",
            Location = "Outpatient",
            Comments = "Bring ID",
        };

        var message = _mapper.ToAppointmentMessage(webhook, "demo-hospital");

        Assert.Equal("appt-1", message.AppointmentUuid);
        Assert.Equal("patient-1", message.PatientUuid);
        Assert.Equal("John Doe", message.PatientName);
        Assert.Equal("Scheduled", message.Status);
        Assert.Equal("Outpatient", message.Location);
        Assert.Contains("General Medicine", message.Instructions);
        Assert.Contains("Bring ID", message.Instructions);
        Assert.Equal("demo-hospital", message.OrganizationKey);
    }

    [Fact]
    public void ToAppointmentMessage_maps_cancelled_event_to_cancelled_status()
    {
        var webhook = new OpenMrsAppointmentWebhook
        {
            Event = "CANCELLED",
            AppointmentUuid = "appt-2",
            Status = "Scheduled",
            StartDateTime = DateTime.UtcNow.AddDays(1),
            PatientUuid = "p",
            PatientName = "Jane",
        };

        var message = _mapper.ToAppointmentMessage(webhook, "default");

        Assert.Equal("Cancelled", message.Status);
    }

    [Fact]
    public void ToAppointmentMessage_requires_appointment_uuid()
    {
        var webhook = new OpenMrsAppointmentWebhook
        {
            Event = "CREATED",
            StartDateTime = DateTime.UtcNow.AddDays(1),
        };

        Assert.Throws<ArgumentException>(() => _mapper.ToAppointmentMessage(webhook, "default"));
    }
}
