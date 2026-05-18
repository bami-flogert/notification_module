namespace NotificationModule.Shared.Persistence;

public sealed class AppointmentRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;

    public string AppointmentUuid { get; set; } = null!;
    public string PatientUuid { get; set; } = null!;
    public string PatientName { get; set; } = null!;
    public string PatientPhone { get; set; } = null!;
    public string PatientEmail { get; set; } = null!;
    public DateTimeOffset StartDateTime { get; set; }
    public string Status { get; set; } = null!;
    public string? Location { get; set; }
    public string? Instructions { get; set; }
    public string SourceSystem { get; set; } = "OpenMRS";
    public string? RawSourcePayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ScheduledNotificationRecord> ScheduledNotifications { get; set; } = new List<ScheduledNotificationRecord>();
}
