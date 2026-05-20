namespace NotificationModule.Shared.Persistence;

/// <summary>Encrypted provider credential row scoped to a single organization.</summary>
public sealed class ProviderSecretRecord
{
    public Guid OrganizationId { get; set; }
    public OrganizationRecord Organization { get; set; } = null!;

    /// <summary>Stable key, e.g. SwiftSend, SecurePost.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>AES-GCM ciphertext concatenated with 16-byte authentication tag.</summary>
    public byte[] EncryptedPayload { get; set; } = null!;

    /// <summary>12-byte AES-GCM nonce.</summary>
    public byte[] Nonce { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
