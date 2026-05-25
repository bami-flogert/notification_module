using Microsoft.EntityFrameworkCore;
using NotificationModule.Producer.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class BillingDeliveriesReportService
{
    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;

    public BillingDeliveriesReportService(IDbContextFactory<NotificationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<BillingDeliveryReportItem>?> GetDeliveriesAsync(
        string organizationKey,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == organizationKey, cancellationToken);
        if (organization is null)
            return null;

        var rows = await db.BillingDeliveryEvents
            .Where(x => x.OrganizationId == organization.Id
                && x.OccurredAt >= from
                && x.OccurredAt <= to)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => Map(organization.Key, row))
            .ToList();
    }

    internal static BillingDeliveryReportItem Map(string organizationKey, BillingDeliveryEventRecord row) =>
        new()
        {
            OrganizationKey = organizationKey,
            Provider = row.Provider,
            ReminderType = row.ReminderType,
            Status = row.Status,
            SentAt = string.Equals(row.Status, "Sent", StringComparison.OrdinalIgnoreCase)
                ? row.OccurredAt
                : null,
            FailedAt = string.Equals(row.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                ? row.OccurredAt
                : null,
            ProviderMessageId = row.ProviderMessageId,
            CorrelationId = row.CorrelationId,
        };
}
