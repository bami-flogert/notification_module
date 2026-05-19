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

    private sealed class InMemoryDbContextFactory<TContext>(DbContextOptions options)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() => (TContext)Activator.CreateInstance(typeof(TContext), options)!;

        public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
