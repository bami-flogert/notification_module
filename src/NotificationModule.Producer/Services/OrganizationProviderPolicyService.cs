using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Messaging;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class OrganizationProviderPolicyService
{
    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;

    public OrganizationProviderPolicyService(IDbContextFactory<NotificationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<OrganizationRecord?> UpdatePolicyAsync(
        string organizationKey,
        string preferredProvider,
        string? fallbackProviders,
        CancellationToken cancellationToken)
    {
        NotificationProviders.ValidateOrThrow(preferredProvider, nameof(preferredProvider));
        var normalizedFallbacks = NotificationProviders.NormalizeFallbackList(fallbackProviders);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == organizationKey, cancellationToken);
        if (organization is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        organization.PreferredProvider = preferredProvider.Trim();
        organization.FallbackProviders = normalizedFallbacks;
        organization.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<bool> HasProviderSecretAsync(
        Guid organizationId,
        string providerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderSecrets.AnyAsync(
            x => x.OrganizationId == organizationId && x.Provider == providerName.Trim(),
            cancellationToken);
    }
}
