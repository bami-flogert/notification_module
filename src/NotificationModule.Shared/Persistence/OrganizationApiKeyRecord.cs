namespace NotificationModule.Shared.Persistence;

public sealed class OrganizationApiKeyRecord
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;

    public byte[] Salt { get; set; } = null!;
    public byte[] KeyHash { get; set; } = null!;
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

