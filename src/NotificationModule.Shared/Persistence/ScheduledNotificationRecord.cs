namespace NotificationModule.Shared.Persistence;

public sealed class ScheduledNotificationRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;
    public Guid AppointmentId { get; set; }
    public AppointmentRecord Appointment { get; set; } = null!;

    public string ReminderType { get; set; } = null!;
    public DateTimeOffset ScheduledSendAt { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
