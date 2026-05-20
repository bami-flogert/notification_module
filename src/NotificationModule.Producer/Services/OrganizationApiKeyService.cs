using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class OrganizationApiKeyService
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;

    public OrganizationApiKeyService(IDbContextFactory<NotificationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<(bool Success, bool Forbidden, Guid? OrganizationId)> ValidateAsync(
        string organizationKey,
        string apiKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(organizationKey) || string.IsNullOrWhiteSpace(apiKey))
            return (false, false, null);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.Key == organizationKey, ct);
        if (org is null)
            return (false, false, null);

        if (!org.IsEnabled)
            return (false, true, org.Id);

        var keys = await db.OrganizationApiKeys
            .Where(x => x.OrganizationId == org.Id && x.IsEnabled)
            .ToListAsync(ct);

        foreach (var key in keys)
        {
            if (Verify(apiKey, key.Salt, key.KeyHash))
                return (true, false, org.Id);
        }

        return (false, false, org.Id);
    }

    public async Task EnsureSeededAsync(
        string organizationKey,
        string plaintextApiKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(organizationKey) || string.IsNullOrWhiteSpace(plaintextApiKey))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.SingleOrDefaultAsync(x => x.Key == organizationKey, ct);
        if (org is null)
        {
            var now = DateTimeOffset.UtcNow;
            org = new OrganizationRecord
            {
                Id = Guid.NewGuid(),
                Key = organizationKey.Trim(),
                Name = organizationKey.Trim(),
                TimeZone = "UTC",
                PreferredProvider = "SwiftSend",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync(ct);
        }

        var exists = await db.OrganizationApiKeys
            .AnyAsync(x => x.OrganizationId == org.Id && x.IsEnabled, ct);
        if (exists)
            return;

        var (salt, hash) = Hash(plaintextApiKey);
        var now2 = DateTimeOffset.UtcNow;
        db.OrganizationApiKeys.Add(new OrganizationApiKeyRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Salt = salt,
            KeyHash = hash,
            IsEnabled = true,
            CreatedAt = now2,
            UpdatedAt = now2,
        });

        await db.SaveChangesAsync(ct);
    }

    private static (byte[] Salt, byte[] Hash) Hash(string apiKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(apiKey),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);
        return (salt, hash);
    }

    private static bool Verify(string apiKey, byte[] salt, byte[] expectedHash)
    {
        var computed = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(apiKey),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
    }
}

