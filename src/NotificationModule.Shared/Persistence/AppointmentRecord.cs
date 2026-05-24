namespace NotificationModule.Shared.Persistence;

public sealed class AppointmentRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;

    public string AppointmentUuid { get; set; } = null!;
    public string PatientUuid { get; set; } = null!;
    public string? PatientName { get; set; }
    public string? PatientPhone { get; set; }
    public string? PatientEmail { get; set; }
    public DateTimeOffset StartDateTime { get; set; }
    public string Status { get; set; } = null!;
    public string? Location { get; set; }
    public string? Instructions { get; set; }
    public string SourceSystem { get; set; } = "OpenMRS";
    public string? RawSourcePayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PiiPurgedAt { get; set; }

    public ICollection<ScheduledNotificationRecord> ScheduledNotifications { get; set; } = new List<ScheduledNotificationRecord>();
    public ICollection<NotificationDeliveryRecord> Deliveries { get; set; } = new List<NotificationDeliveryRecord>();
}
