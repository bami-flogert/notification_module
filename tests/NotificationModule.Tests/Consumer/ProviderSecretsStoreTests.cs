using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Consumer;

public sealed class ProviderSecretsStoreTests
{
    [Fact]
    public async Task GetForOrganizationAsync_loads_secrets_per_organization()
    {
        var config = TestDb.CreateConfiguration();
        var protector = new AesGcmSecretProtector(config);
        var dbFactory = TestDb.CreateSecretsFactory();
        await SeedOrganizationSecretsAsync(dbFactory, protector, "default", swiftApiKey: "default-swift");
        await SeedOrganizationSecretsAsync(dbFactory, protector, "other", swiftApiKey: "other-swift");

        var store = new ProviderSecretsStore(dbFactory, protector, config, NullLogger<ProviderSecretsStore>.Instance);

        var defaultSecrets = await store.GetForOrganizationAsync("default");
        var otherSecrets = await store.GetForOrganizationAsync("other");

        Assert.Equal("default-swift", defaultSecrets.SwiftSend.ApiKey);
        Assert.Equal("other-swift", otherSecrets.SwiftSend.ApiKey);
    }

    [Fact]
    public async Task GetForOrganizationAsync_throws_when_provider_row_is_missing()
    {
        var config = TestDb.CreateConfiguration();
        var protector = new AesGcmSecretProtector(config);
        var dbFactory = TestDb.CreateSecretsFactory();
        await SeedOrganizationSecretsAsync(
            dbFactory,
            protector,
            "partial",
            includeAsyncFlow: false);

        var store = new ProviderSecretsStore(dbFactory, protector, config, NullLogger<ProviderSecretsStore>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.GetForOrganizationAsync("partial"));

        Assert.Contains("AsyncFlow", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedOrganizationSecretsAsync(
        IDbContextFactory<SecretsDbContext> dbFactory,
        AesGcmSecretProtector protector,
        string organizationKey,
        string swiftApiKey = "swift-key",
        bool includeAsyncFlow = true)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync();

        var organization = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Key = organizationKey,
            Name = organizationKey,
            TimeZone = "UTC",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Organizations.Add(organization);

        AddSecret(db, organization.Id, ProviderSecretKeys.SwiftSend, new SwiftSendSecretPayload { ApiKey = swiftApiKey }, protector, now);
        AddSecret(db, organization.Id, ProviderSecretKeys.SecurePost, new SecurePostSecretPayload { ClientId = "id", ClientSecret = "secret" }, protector, now);
        AddSecret(db, organization.Id, ProviderSecretKeys.LegacyLink, new LegacyLinkSecretPayload { Username = "user", Password = "pass" }, protector, now);
        if (includeAsyncFlow)
            AddSecret(db, organization.Id, ProviderSecretKeys.AsyncFlow, new AsyncFlowSecretPayload { ApiKey = "async" }, protector, now);

        await db.SaveChangesAsync();
    }

    private static void AddSecret(
        SecretsDbContext db,
        Guid organizationId,
        string provider,
        object payload,
        AesGcmSecretProtector protector,
        DateTimeOffset now)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var (cipher, nonce) = protector.Encrypt(plaintext);
        db.ProviderSecrets.Add(new ProviderSecretRecord
        {
            OrganizationId = organizationId,
            Provider = provider,
            EncryptedPayload = cipher,
            Nonce = nonce,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
