using Microsoft.EntityFrameworkCore;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class OrganizationProviderPolicyServiceTests
{
    [Fact]
    public async Task UpdatePolicyAsync_persists_valid_policy()
    {
        var factory = TestDb.CreateNotificationFactory();
        await SeedOrganizationAsync(factory, "test-org");

        var service = new OrganizationProviderPolicyService(factory);
        var updated = await service.UpdatePolicyAsync(
            "test-org",
            "LegacyLink",
            "AsyncFlow,SecurePost",
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("LegacyLink", updated.PreferredProvider);
        Assert.Equal("AsyncFlow,SecurePost", updated.FallbackProviders);

        await using var db = await factory.CreateDbContextAsync();
        var organization = await db.Organizations.SingleAsync();
        Assert.Equal("LegacyLink", organization.PreferredProvider);
        Assert.Equal("AsyncFlow,SecurePost", organization.FallbackProviders);
    }

    [Fact]
    public async Task UpdatePolicyAsync_returns_null_for_unknown_organization()
    {
        var factory = TestDb.CreateNotificationFactory();
        var service = new OrganizationProviderPolicyService(factory);

        var updated = await service.UpdatePolicyAsync(
            "missing-org",
            "SwiftSend",
            null,
            CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task UpdatePolicyAsync_rejects_unknown_preferred_provider()
    {
        var factory = TestDb.CreateNotificationFactory();
        await SeedOrganizationAsync(factory, "test-org");

        var service = new OrganizationProviderPolicyService(factory);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdatePolicyAsync("test-org", "LegacyLinkX", null, CancellationToken.None));

        Assert.Contains("LegacyLinkX", ex.Message);
    }

    [Fact]
    public async Task UpdatePolicyAsync_rejects_invalid_fallback_provider()
    {
        var factory = TestDb.CreateNotificationFactory();
        await SeedOrganizationAsync(factory, "test-org");

        var service = new OrganizationProviderPolicyService(factory);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdatePolicyAsync("test-org", "SwiftSend", "LegacyLink,BadProvider", CancellationToken.None));

        Assert.Contains("BadProvider", ex.Message);
    }

    [Fact]
    public async Task HasProviderSecretAsync_returns_true_when_row_exists()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = await SeedOrganizationAsync(factory, "test-org");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProviderSecrets.Add(new ProviderSecretRecord
            {
                OrganizationId = orgId,
                Provider = "SwiftSend",
                EncryptedPayload = [1],
                Nonce = new byte[12],
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = new OrganizationProviderPolicyService(factory);
        var hasSecret = await service.HasProviderSecretAsync(orgId, "SwiftSend", CancellationToken.None);

        Assert.True(hasSecret);
    }

    private static async Task<Guid> SeedOrganizationAsync(
        IDbContextFactory<NotificationDbContext> factory,
        string organizationKey)
    {
        var orgId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new OrganizationRecord
        {
            Id = orgId,
            Key = organizationKey,
            Name = organizationKey,
            TimeZone = "UTC",
            PreferredProvider = "SwiftSend",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return orgId;
    }
}
