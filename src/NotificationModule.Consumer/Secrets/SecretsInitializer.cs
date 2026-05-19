using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Persistence;

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

        var organization = await EnsureDefaultOrganizationAsync(db, cancellationToken);
        await EnsureProviderSecretsAsync(db, organization, cancellationToken);

        await _secretsStore.ReloadAsync(cancellationToken);
    }

    private async Task EnsureProviderSecretsAsync(
        SecretsDbContext db,
        OrganizationRecord organization,
        CancellationToken cancellationToken)
    {
        var existing = await db.ProviderSecrets
            .Where(x => x.OrganizationId == organization.Id)
            .Select(x => x.Provider)
            .ToListAsync(cancellationToken);

        var missing = ProviderSecretKeys.All
            .Except(existing, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0)
            return;

        _logger.LogInformation(
            "Organization '{OrganizationKey}' is missing provider secrets for: {Providers}. Attempting seed from configuration.",
            organization.Key,
            string.Join(", ", missing));

        var seeded = SeedMissingFromConfiguration(db, organization.Id, missing, DateTimeOffset.UtcNow);
        if (seeded.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Seeded encrypted provider secrets for organization '{OrganizationKey}': {Providers}",
                organization.Key,
                string.Join(", ", seeded));
        }

        var stillMissing = ProviderSecretKeys.All
            .Except(
                existing.Concat(seeded),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (stillMissing.Count == 0)
            return;

        throw new InvalidOperationException(
            $"Organization '{organization.Key}' is missing provider secrets for: {string.Join(", ", stillMissing)}. " +
            "Configure SecretsSeed:* values (e.g. env SecretsSeed__SwiftSend__ApiKey) for a one-time encrypted seed, " +
            "or insert the missing rows manually.");
    }

    private List<string> SeedMissingFromConfiguration(
        SecretsDbContext db,
        Guid organizationId,
        IReadOnlyList<string> missingProviders,
        DateTimeOffset now)
    {
        var seeded = new List<string>();

        foreach (var provider in missingProviders)
        {
            if (!TryCreateSecretPayload(provider, out var payload))
                continue;

            AddSecretRow(db, organizationId, provider, payload, now);
            seeded.Add(provider);
        }

        return seeded;
    }

    private bool TryCreateSecretPayload(string provider, out byte[] plaintextJson)
    {
        switch (provider)
        {
            case ProviderSecretKeys.SwiftSend:
            {
                var apiKey = _configuration["SecretsSeed:SwiftSend:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    plaintextJson = [];
                    return false;
                }

                plaintextJson = JsonSerializer.SerializeToUtf8Bytes(
                    new SwiftSendSecretPayload { ApiKey = apiKey },
                    JsonOptions);
                return true;
            }
            case ProviderSecretKeys.SecurePost:
            {
                var clientId = _configuration["SecretsSeed:SecurePost:ClientId"];
                var clientSecret = _configuration["SecretsSeed:SecurePost:ClientSecret"];
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    plaintextJson = [];
                    return false;
                }

                plaintextJson = JsonSerializer.SerializeToUtf8Bytes(
                    new SecurePostSecretPayload { ClientId = clientId, ClientSecret = clientSecret },
                    JsonOptions);
                return true;
            }
            case ProviderSecretKeys.LegacyLink:
            {
                var username = _configuration["SecretsSeed:LegacyLink:Username"];
                var password = _configuration["SecretsSeed:LegacyLink:Password"];
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    plaintextJson = [];
                    return false;
                }

                plaintextJson = JsonSerializer.SerializeToUtf8Bytes(
                    new LegacyLinkSecretPayload { Username = username, Password = password },
                    JsonOptions);
                return true;
            }
            case ProviderSecretKeys.AsyncFlow:
            {
                var apiKey = _configuration["SecretsSeed:AsyncFlow:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    plaintextJson = [];
                    return false;
                }

                plaintextJson = JsonSerializer.SerializeToUtf8Bytes(
                    new AsyncFlowSecretPayload { ApiKey = apiKey },
                    JsonOptions);
                return true;
            }
            default:
                plaintextJson = [];
                return false;
        }
    }

    private async Task<OrganizationRecord> EnsureDefaultOrganizationAsync(
        SecretsDbContext db,
        CancellationToken cancellationToken)
    {
        var key = _configuration["Organizations:Default:Key"] ?? "default";
        var name = _configuration["Organizations:Default:Name"] ?? "Default Organization";
        var timeZone = _configuration["Organizations:Default:TimeZone"] ?? "UTC";
        var openMrsBaseUrl = _configuration["Organizations:Default:OpenMrsBaseUrl"];
        var now = DateTimeOffset.UtcNow;

        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (organization is not null)
        {
            organization.Name = name;
            organization.TimeZone = timeZone;
            organization.OpenMrsBaseUrl = openMrsBaseUrl;
            organization.IsEnabled = true;
            organization.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return organization;
        }

        organization = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            TimeZone = timeZone,
            OpenMrsBaseUrl = openMrsBaseUrl,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private void AddSecretRow(SecretsDbContext db, Guid organizationId, string provider, byte[] plaintextJson, DateTimeOffset now)
    {
        var (cipher, nonce) = _protector.Encrypt(plaintextJson);
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
