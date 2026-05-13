using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotificationModule.Consumer.Secrets;

/// <summary>Applies EF migrations, optionally seeds encrypted rows from env/config, then loads secrets into memory.</summary>
public sealed class SecretsInitializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDbContextFactory<SecretsDbContext> _dbFactory;
    private readonly AesGcmSecretProtector _protector;
    private readonly ProviderSecretsStore _secretsStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecretsInitializer> _logger;

    public SecretsInitializer(
        IDbContextFactory<SecretsDbContext> dbFactory,
        AesGcmSecretProtector protector,
        ProviderSecretsStore secretsStore,
        IConfiguration configuration,
        ILogger<SecretsInitializer> logger)
    {
        _dbFactory = dbFactory;
        _protector = protector;
        _secretsStore = secretsStore;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);

        var count = await db.ProviderSecrets.CountAsync(cancellationToken);
        if (count == 0)
        {
            _logger.LogInformation("No provider secrets in database; attempting one-time seed from SecretsSeed configuration.");
            await SeedFromConfigurationAsync(db, cancellationToken);
        }

        await _secretsStore.ReloadAsync(_dbFactory, _protector, cancellationToken);
    }

    private async Task SeedFromConfigurationAsync(SecretsDbContext db, CancellationToken cancellationToken)
    {
        var swiftApiKey = _configuration["SecretsSeed:SwiftSend:ApiKey"];
        var secureClientId = _configuration["SecretsSeed:SecurePost:ClientId"];
        var secureClientSecret = _configuration["SecretsSeed:SecurePost:ClientSecret"];
        var legacyUser = _configuration["SecretsSeed:LegacyLink:Username"];
        var legacyPass = _configuration["SecretsSeed:LegacyLink:Password"];
        var asyncKey = _configuration["SecretsSeed:AsyncFlow:ApiKey"];

        if (string.IsNullOrWhiteSpace(swiftApiKey)
            || string.IsNullOrWhiteSpace(secureClientId)
            || string.IsNullOrWhiteSpace(secureClientSecret)
            || string.IsNullOrWhiteSpace(legacyUser)
            || string.IsNullOrWhiteSpace(legacyPass)
            || string.IsNullOrWhiteSpace(asyncKey))
        {
            throw new InvalidOperationException(
                "Database has no provider secrets. Configure SecretsSeed:* (e.g. env SecretsSeed__SwiftSend__ApiKey) " +
                "for a one-time encrypted seed, or insert rows manually.");
        }

        var now = DateTimeOffset.UtcNow;

        AddSecretRow(db, ProviderSecretKeys.SwiftSend, JsonSerializer.SerializeToUtf8Bytes(new SwiftSendSecretPayload { ApiKey = swiftApiKey }, JsonOptions), now);
        AddSecretRow(db, ProviderSecretKeys.SecurePost, JsonSerializer.SerializeToUtf8Bytes(new SecurePostSecretPayload { ClientId = secureClientId, ClientSecret = secureClientSecret }, JsonOptions), now);
        AddSecretRow(db, ProviderSecretKeys.LegacyLink, JsonSerializer.SerializeToUtf8Bytes(new LegacyLinkSecretPayload { Username = legacyUser, Password = legacyPass }, JsonOptions), now);
        AddSecretRow(db, ProviderSecretKeys.AsyncFlow, JsonSerializer.SerializeToUtf8Bytes(new AsyncFlowSecretPayload { ApiKey = asyncKey }, JsonOptions), now);

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded encrypted provider secrets (values not logged).");
    }

    private void AddSecretRow(SecretsDbContext db, string provider, byte[] plaintextJson, DateTimeOffset now)
    {
        var (cipher, nonce) = _protector.Encrypt(plaintextJson);
        db.ProviderSecrets.Add(new ProviderSecretRecord
        {
            Provider = provider,
            EncryptedPayload = cipher,
            Nonce = nonce,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
