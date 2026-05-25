using System.Text.Json;
using NotificationModule.Producer.Models;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class BillingDeliveriesReportServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetDeliveriesAsync_returns_mapped_rows_in_date_range()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = await SeedOrganizationAsync(factory, "test-org");
        var sentCorrelationId = Guid.NewGuid();
        var failedCorrelationId = Guid.NewGuid();

        await SeedBillingEventAsync(
            factory,
            orgId,
            provider: "SwiftSend",
            reminderType: "24h",
            status: "Sent",
            occurredAt: BaseTime,
            correlationId: sentCorrelationId,
            providerMessageId: "msg-123");
        await SeedBillingEventAsync(
            factory,
            orgId,
            provider: "LegacyLink",
            reminderType: "1h",
            status: "Failed",
            occurredAt: BaseTime.AddHours(1),
            correlationId: failedCorrelationId);

        var service = new BillingDeliveriesReportService(factory);
        var results = await service.GetDeliveriesAsync(
            "test-org",
            BaseTime.AddHours(-1),
            BaseTime.AddHours(2),
            CancellationToken.None);

        Assert.NotNull(results);
        Assert.Equal(2, results.Count);

        var sent = results[0];
        Assert.Equal("test-org", sent.OrganizationKey);
        Assert.Equal("SwiftSend", sent.Provider);
        Assert.Equal("24h", sent.ReminderType);
        Assert.Equal("Sent", sent.Status);
        Assert.Equal(BaseTime, sent.SentAt);
        Assert.Null(sent.FailedAt);
        Assert.Equal("msg-123", sent.ProviderMessageId);
        Assert.Equal(sentCorrelationId, sent.CorrelationId);

        var failed = results[1];
        Assert.Equal("LegacyLink", failed.Provider);
        Assert.Equal("1h", failed.ReminderType);
        Assert.Equal("Failed", failed.Status);
        Assert.Null(failed.SentAt);
        Assert.Equal(BaseTime.AddHours(1), failed.FailedAt);
        Assert.Equal(failedCorrelationId, failed.CorrelationId);
    }

    [Fact]
    public async Task GetDeliveriesAsync_excludes_other_organizations()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgA = await SeedOrganizationAsync(factory, "org-a");
        var orgB = await SeedOrganizationAsync(factory, "org-b");

        await SeedBillingEventAsync(factory, orgA, "SwiftSend", "24h", "Sent", BaseTime);
        await SeedBillingEventAsync(factory, orgB, "SwiftSend", "24h", "Sent", BaseTime);

        var service = new BillingDeliveriesReportService(factory);
        var results = await service.GetDeliveriesAsync(
            "org-a",
            BaseTime.AddDays(-1),
            BaseTime.AddDays(1),
            CancellationToken.None);

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("org-a", results[0].OrganizationKey);
    }

    [Fact]
    public async Task GetDeliveriesAsync_excludes_out_of_range_events()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = await SeedOrganizationAsync(factory, "test-org");

        await SeedBillingEventAsync(factory, orgId, "SwiftSend", "24h", "Sent", BaseTime.AddDays(-2));
        await SeedBillingEventAsync(factory, orgId, "SwiftSend", "24h", "Sent", BaseTime);
        await SeedBillingEventAsync(factory, orgId, "SwiftSend", "24h", "Sent", BaseTime.AddDays(2));

        var service = new BillingDeliveriesReportService(factory);
        var results = await service.GetDeliveriesAsync(
            "test-org",
            BaseTime.AddDays(-1),
            BaseTime.AddDays(1),
            CancellationToken.None);

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal(BaseTime, results[0].SentAt);
    }

    [Fact]
    public async Task GetDeliveriesAsync_returns_null_for_unknown_organization()
    {
        var factory = TestDb.CreateNotificationFactory();
        var service = new BillingDeliveriesReportService(factory);

        var results = await service.GetDeliveriesAsync(
            "missing-org",
            BaseTime.AddDays(-1),
            BaseTime.AddDays(1),
            CancellationToken.None);

        Assert.Null(results);
    }

    [Fact]
    public async Task GetDeliveriesAsync_response_does_not_surface_pii_fields()
    {
        var factory = TestDb.CreateNotificationFactory();
        var orgId = await SeedOrganizationAsync(factory, "test-org");
        await SeedBillingEventAsync(factory, orgId, "SwiftSend", "24h", "Sent", BaseTime, providerMessageId: "msg-1");

        var propertyNames = typeof(BillingDeliveryReportItem)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain("PatientName", propertyNames);
        Assert.DoesNotContain("PatientPhone", propertyNames);
        Assert.DoesNotContain("PatientEmail", propertyNames);
        Assert.DoesNotContain("AppointmentUuid", propertyNames);

        var service = new BillingDeliveriesReportService(factory);
        var results = await service.GetDeliveriesAsync(
            "test-org",
            BaseTime.AddDays(-1),
            BaseTime.AddDays(1),
            CancellationToken.None);

        var json = JsonSerializer.Serialize(results![0]);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal(8, root.EnumerateObject().Count());
        Assert.False(root.TryGetProperty("patientName", out _));
        Assert.False(root.TryGetProperty("appointmentUuid", out _));
    }

    private static async Task<Guid> SeedOrganizationAsync(
        IDbContextFactory<NotificationDbContext> factory,
        string organizationKey)
    {
        var orgId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new OrganizationRecord
        {
            Id = orgId,
            Key = organizationKey,
            Name = organizationKey,
            TimeZone = "UTC",
            PreferredProvider = "SwiftSend",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return orgId;
    }

    private static async Task SeedBillingEventAsync(
        IDbContextFactory<NotificationDbContext> factory,
        Guid organizationId,
        string provider,
        string reminderType,
        string status,
        DateTimeOffset occurredAt,
        Guid? correlationId = null,
        string? providerMessageId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.BillingDeliveryEvents.Add(new BillingDeliveryEventRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Provider = provider,
            ReminderType = reminderType,
            Status = status,
            OccurredAt = occurredAt,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            ProviderMessageId = providerMessageId,
        });
        await db.SaveChangesAsync();
    }
}
