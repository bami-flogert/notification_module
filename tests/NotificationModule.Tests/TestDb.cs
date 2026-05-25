using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests;

internal static class TestDb
{
    public static IDbContextFactory<NotificationDbContext> CreateNotificationFactory(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new InMemoryDbContextFactory<NotificationDbContext>(options);
    }

    public static IDbContextFactory<SecretsDbContext> CreateSecretsFactory(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<SecretsDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new InMemoryDbContextFactory<SecretsDbContext>(options);
    }

    public static IConfiguration CreateConfiguration(
        Action<Dictionary<string, string?>>? configure = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Organizations:Default:Key"] = "default",
            ["Organizations:Default:TimeZone"] = "UTC",
            ["Providers:SwiftSend:BaseUrl"] = "http://comworld:8080",
            ["Providers:StudentGroup"] = "test-group",
            ["Secrets:MasterKeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["SecretsSeed:SwiftSend:ApiKey"] = "swift-key",
            ["SecretsSeed:SecurePost:ClientId"] = "secure-id",
            ["SecretsSeed:SecurePost:ClientSecret"] = "secure-secret",
            ["SecretsSeed:LegacyLink:Username"] = "legacy-user",
            ["SecretsSeed:LegacyLink:Password"] = "legacy-pass",
            ["SecretsSeed:AsyncFlow:ApiKey"] = "async-key",
        };

        configure?.Invoke(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static async Task SeedOrganizationSecretsAsync(
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

        AddProviderSecret(db, organization.Id, ProviderSecretKeys.SwiftSend, new SwiftSendSecretPayload { ApiKey = swiftApiKey }, protector, now);
        AddProviderSecret(db, organization.Id, ProviderSecretKeys.SecurePost, new SecurePostSecretPayload { ClientId = "id", ClientSecret = "secret" }, protector, now);
        AddProviderSecret(db, organization.Id, ProviderSecretKeys.LegacyLink, new LegacyLinkSecretPayload { Username = "user", Password = "pass" }, protector, now);
        if (includeAsyncFlow)
            AddProviderSecret(db, organization.Id, ProviderSecretKeys.AsyncFlow, new AsyncFlowSecretPayload { ApiKey = "async" }, protector, now);

        await db.SaveChangesAsync();
    }

    private static void AddProviderSecret(
        SecretsDbContext db,
        Guid organizationId,
        string provider,
        object payload,
        AesGcmSecretProtector protector,
        DateTimeOffset now)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
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

    private sealed class InMemoryDbContextFactory<TContext>(DbContextOptions options)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => (TContext)Activator.CreateInstance(typeof(TContext), options)!;

        public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
