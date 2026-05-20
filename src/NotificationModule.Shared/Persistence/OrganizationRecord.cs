namespace NotificationModule.Shared.Persistence;

public sealed class OrganizationRecord
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string TimeZone { get; set; } = "UTC";
    public string? OpenMrsBaseUrl { get; set; }
    public bool IsEnabled { get; set; } = true;

    public string PreferredProvider { get; set; } = "SwiftSend";
    public string? FallbackProviders { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ProviderSecretRecord> ProviderSecrets { get; set; } = new List<ProviderSecretRecord>();
    public ICollection<AppointmentRecord> Appointments { get; set; } = new List<AppointmentRecord>();
    public ICollection<OrganizationApiKeyRecord> ApiKeys { get; set; } = new List<OrganizationApiKeyRecord>();
}
