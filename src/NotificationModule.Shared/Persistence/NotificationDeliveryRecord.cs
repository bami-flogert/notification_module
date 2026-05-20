namespace NotificationModule.Shared.Persistence;

public sealed class NotificationDeliveryRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;
    public Guid AppointmentId { get; set; }
    public AppointmentRecord Appointment { get; set; } = null!;
    public Guid ScheduledNotificationId { get; set; }
    public ScheduledNotificationRecord ScheduledNotification { get; set; } = null!;

    public string Provider { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
