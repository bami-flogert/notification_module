namespace NotificationModule.Consumer.Secrets;

/// <summary>Encrypted provider credential row (single-tenant / global config).</summary>
public sealed class ProviderSecretRecord
{
    /// <summary>Stable key, e.g. SwiftSend, SecurePost.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>AES-GCM ciphertext concatenated with 16-byte authentication tag.</summary>
    public byte[] EncryptedPayload { get; set; } = null!;

    /// <summary>12-byte AES-GCM nonce.</summary>
    public byte[] Nonce { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
