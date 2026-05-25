namespace NotificationModule.Shared.Persistence;

public sealed class BillingDeliveryEventRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Provider { get; set; } = null!;
    public string ReminderType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }

    // Opaque GUID — no foreign key to appointments or patients.
    public Guid CorrelationId { get; set; }
    public string? ProviderMessageId { get; set; }
}
