using System.Security.Cryptography;

namespace NotificationModule.Consumer.Secrets;

/// <summary>AES-256-GCM encrypt/decrypt for secret payloads at rest.</summary>
public sealed class AesGcmSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var b64 = configuration["Secrets:MasterKeyBase64"];
        if (string.IsNullOrWhiteSpace(b64))
        {
            throw new InvalidOperationException(
                "Secrets:MasterKeyBase64 is required (set env Secrets__MasterKeyBase64 to a Base64-encoded 32-byte key).");
        }

        _key = Convert.FromBase64String(b64);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "Secrets:MasterKeyBase64 must decode to exactly 32 bytes (AES-256).");
        }
    }

    /// <summary>Returns ciphertext with tag appended (length = plaintext.Length + 16).</summary>
    public (byte[] ciphertextWithTag, byte[] nonce) Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        return (combined, nonce);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertextWithTag, ReadOnlySpan<byte> nonce)
    {
        if (ciphertextWithTag.Length < TagSize)
            throw new CryptographicException("Encrypted payload is too short.");
        if (nonce.Length != NonceSize)
            throw new CryptographicException("Invalid nonce length.");

        var cipherLen = ciphertextWithTag.Length - TagSize;
        var plaintext = new byte[cipherLen];

        using var aes = new AesGcm(_key);
        aes.Decrypt(
            nonce,
            ciphertextWithTag[..cipherLen],
            ciphertextWithTag[cipherLen..],
            plaintext);

        return plaintext;
    }
}
