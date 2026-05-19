using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotificationModule.Consumer.Secrets;

/// <summary>Per-organization decrypted provider secrets loaded from Postgres on demand.</summary>
public sealed class ProviderSecretsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDbContextFactory<SecretsDbContext> _dbFactory;
    private readonly AesGcmSecretProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProviderSecretsStore> _logger;
    private readonly ConcurrentDictionary<string, OrganizationProviderSecrets> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _loadLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public ProviderSecretsStore(
        IDbContextFactory<SecretsDbContext> dbFactory,
        AesGcmSecretProtector protector,
        IConfiguration configuration,
        ILogger<ProviderSecretsStore> logger)
    {
        _dbFactory = dbFactory;
        _protector = protector;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OrganizationProviderSecrets> GetForOrganizationAsync(
        string organizationKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationKey))
            throw new ArgumentException("Organization key is required.", nameof(organizationKey));

        if (_cache.TryGetValue(organizationKey, out var cached))
            return cached;

        var loadLock = _loadLocks.GetOrAdd(organizationKey, _ => new SemaphoreSlim(1, 1));
        await loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(organizationKey, out cached))
                return cached;

            var loaded = await LoadOrganizationSecretsAsync(organizationKey, cancellationToken);
            _cache[organizationKey] = loaded;
            _logger.LogInformation(
                "Provider secrets loaded for organization '{OrganizationKey}' (in memory only; never log values).",
                organizationKey);
            return loaded;
        }
        finally
        {
            loadLock.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();

        var defaultKey = _configuration["Organizations:Default:Key"] ?? "default";
        await GetForOrganizationAsync(defaultKey, cancellationToken);
    }

    private async Task<OrganizationProviderSecrets> LoadOrganizationSecretsAsync(
        string organizationKey,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProviderSecrets
            .AsNoTracking()
            .Where(x => x.Organization.Key == organizationKey)
            .ToListAsync(cancellationToken);

        SwiftSendSecretPayload? swift = null;
        SecurePostSecretPayload? secure = null;
        LegacyLinkSecretPayload? legacy = null;
        AsyncFlowSecretPayload? async = null;

        foreach (var row in rows)
        {
            var plain = _protector.Decrypt(row.EncryptedPayload, row.Nonce);
            switch (row.Provider)
            {
                case ProviderSecretKeys.SwiftSend:
                    swift = JsonSerializer.Deserialize<SwiftSendSecretPayload>(plain, JsonOptions);
                    break;
                case ProviderSecretKeys.SecurePost:
                    secure = JsonSerializer.Deserialize<SecurePostSecretPayload>(plain, JsonOptions);
                    break;
                case ProviderSecretKeys.LegacyLink:
                    legacy = JsonSerializer.Deserialize<LegacyLinkSecretPayload>(plain, JsonOptions);
                    break;
                case ProviderSecretKeys.AsyncFlow:
                    async = JsonSerializer.Deserialize<AsyncFlowSecretPayload>(plain, JsonOptions);
                    break;
                default:
                    _logger.LogWarning("Unknown provider secret row: {Provider}", row.Provider);
                    break;
            }
        }

        var secrets = new OrganizationProviderSecrets
        {
            SwiftSend = swift ?? throw new InvalidOperationException(
                $"Missing SwiftSend row in provider_secrets for organization '{organizationKey}'."),
            SecurePost = secure ?? throw new InvalidOperationException(
                $"Missing SecurePost row in provider_secrets for organization '{organizationKey}'."),
            LegacyLink = legacy ?? throw new InvalidOperationException(
                $"Missing LegacyLink row in provider_secrets for organization '{organizationKey}'."),
            AsyncFlow = async ?? throw new InvalidOperationException(
                $"Missing AsyncFlow row in provider_secrets for organization '{organizationKey}'."),
        };

        ValidatePayload(secrets.SwiftSend, ProviderSecretKeys.SwiftSend);
        ValidatePayload(secrets.SecurePost, ProviderSecretKeys.SecurePost);
        ValidatePayload(secrets.LegacyLink, ProviderSecretKeys.LegacyLink);
        ValidatePayload(secrets.AsyncFlow, ProviderSecretKeys.AsyncFlow);

        return secrets;
    }

    private static void ValidatePayload(SwiftSendSecretPayload p, string name)
    {
        if (string.IsNullOrWhiteSpace(p.ApiKey))
            throw new InvalidOperationException($"{name}: apiKey is missing.");
    }

    private static void ValidatePayload(SecurePostSecretPayload p, string name)
    {
        if (string.IsNullOrWhiteSpace(p.ClientId) || string.IsNullOrWhiteSpace(p.ClientSecret))
            throw new InvalidOperationException($"{name}: clientId/clientSecret missing.");
    }

    private static void ValidatePayload(LegacyLinkSecretPayload p, string name)
    {
        if (string.IsNullOrWhiteSpace(p.Username) || string.IsNullOrWhiteSpace(p.Password))
            throw new InvalidOperationException($"{name}: username/password missing.");
    }

    private static void ValidatePayload(AsyncFlowSecretPayload p, string name)
    {
        if (string.IsNullOrWhiteSpace(p.ApiKey))
            throw new InvalidOperationException($"{name}: apiKey is missing.");
    }
}
