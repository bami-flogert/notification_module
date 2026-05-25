namespace NotificationModule.Producer.Models;

public sealed class BillingDeliveryReportItem
{
    public required string OrganizationKey { get; init; }
    public required string Provider { get; init; }
    public required string ReminderType { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? FailedAt { get; init; }
    public string? ProviderMessageId { get; init; }
    public Guid CorrelationId { get; init; }
}
