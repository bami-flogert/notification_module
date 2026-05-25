using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Services;

public sealed class DataRetentionWorker : BackgroundService
{
    private readonly IDbContextFactory<NotificationDbContext> _dbFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataRetentionWorker> _logger;

    public DataRetentionWorker(
        IDbContextFactory<NotificationDbContext> dbFactory,
        IConfiguration configuration,
        ILogger<DataRetentionWorker> logger)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(
            Math.Max(1, _configuration.GetValue("DataRetention:RunIntervalHours", 24)));
        var retentionDays = Math.Max(1, _configuration.GetValue("DataRetention:RetentionDays", 14));
        var billingRetentionDays = Math.Max(1, _configuration.GetValue("DataRetention:BillingRetentionDays", 365));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredPiiAsync(retentionDays, stoppingToken);
                await PurgeBillingEventsAsync(billingRetentionDays, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention purge failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PurgeExpiredPiiAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var purgedCount = await DataRetentionPurge.PurgeExpiredPiiAsync(db, cutoff, cancellationToken);

        if (purgedCount > 0)
            _logger.LogInformation(
                "Purged PII from {Count} appointments older than {RetentionDays} days.",
                purgedCount,
                retentionDays);
    }

    private async Task PurgeBillingEventsAsync(int billingRetentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-billingRetentionDays);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var deletedCount = await DataRetentionPurge.PurgeBillingEventsOlderThanAsync(db, cutoff, cancellationToken);

        if (deletedCount > 0)
            _logger.LogInformation(
                "Deleted {Count} billing events older than {BillingRetentionDays} days.",
                deletedCount,
                billingRetentionDays);
    }
}
