using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace NotificationModule.Consumer.Secrets;

/// <summary>In-memory decrypted provider secrets loaded from Postgres.</summary>
public sealed class ProviderSecretsStore
{
    private readonly ILogger<ProviderSecretsStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ProviderSecretsStore(ILogger<ProviderSecretsStore> logger)
    {
        _logger = logger;
    }

    private SwiftSendSecretPayload? _swiftSend;
    private SecurePostSecretPayload? _securePost;
    private LegacyLinkSecretPayload? _legacyLink;
    private AsyncFlowSecretPayload? _asyncFlow;

    public SwiftSendSecretPayload SwiftSend =>
        _swiftSend ?? throw new InvalidOperationException("SwiftSend secrets are not loaded.");

    public SecurePostSecretPayload SecurePost =>
        _securePost ?? throw new InvalidOperationException("SecurePost secrets are not loaded.");

    public LegacyLinkSecretPayload LegacyLink =>
        _legacyLink ?? throw new InvalidOperationException("LegacyLink secrets are not loaded.");

    public AsyncFlowSecretPayload AsyncFlow =>
        _asyncFlow ?? throw new InvalidOperationException("AsyncFlow secrets are not loaded.");

    public async Task ReloadAsync(
        IDbContextFactory<SecretsDbContext> dbFactory,
        AesGcmSecretProtector protector,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProviderSecrets.AsNoTracking().ToListAsync(cancellationToken);

        SwiftSendSecretPayload? swift = null;
        SecurePostSecretPayload? secure = null;
        LegacyLinkSecretPayload? legacy = null;
        AsyncFlowSecretPayload? async = null;

        foreach (var row in rows)
        {
            var plain = protector.Decrypt(row.EncryptedPayload, row.Nonce);
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

        _swiftSend = swift ?? throw new InvalidOperationException("Missing SwiftSend row in provider_secrets.");
        _securePost = secure ?? throw new InvalidOperationException("Missing SecurePost row in provider_secrets.");
        _legacyLink = legacy ?? throw new InvalidOperationException("Missing LegacyLink row in provider_secrets.");
        _asyncFlow = async ?? throw new InvalidOperationException("Missing AsyncFlow row in provider_secrets.");

        ValidatePayload(_swiftSend, nameof(SwiftSend));
        ValidatePayload(_securePost, nameof(SecurePost));
        ValidatePayload(_legacyLink, nameof(LegacyLink));
        ValidatePayload(_asyncFlow, nameof(AsyncFlow));

        _logger.LogInformation("Provider secrets loaded from database (in memory only; never log values).");
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
