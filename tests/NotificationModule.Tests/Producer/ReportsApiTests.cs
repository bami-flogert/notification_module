using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Producer;

public sealed class ReportsApiTests : IClassFixture<ProducerWebApplicationFactory>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly ProducerWebApplicationFactory _factory;

    public ReportsApiTests(ProducerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDeliveries_returns_unauthorized_without_api_key()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/reports/deliveries?organizationKey={ProducerWebApplicationFactory.DefaultOrganizationKey}" +
            $"&from={Uri.EscapeDataString(BaseTime.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(BaseTime.AddDays(1).ToString("O"))}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveries_returns_unauthorized_when_organization_does_not_exist()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ProducerWebApplicationFactory.TestApiKey);

        using var response = await client.GetAsync(
            "/api/reports/deliveries?organizationKey=missing-org" +
            $"&from={Uri.EscapeDataString(BaseTime.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(BaseTime.AddDays(1).ToString("O"))}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveries_returns_pii_free_billing_rows()
    {
        await SeedBillingEventAsync("msg-http-123");

        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(
            $"/api/reports/deliveries?organizationKey={ProducerWebApplicationFactory.DefaultOrganizationKey}" +
            $"&from={Uri.EscapeDataString(BaseTime.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(BaseTime.AddDays(1).ToString("O"))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(1, rows.GetArrayLength());

        var row = rows[0];
        Assert.Equal("SwiftSend", row.GetProperty("provider").GetString());
        Assert.Equal("Sent", row.GetProperty("status").GetString());
        Assert.Equal("msg-http-123", row.GetProperty("providerMessageId").GetString());
        Assert.False(row.TryGetProperty("patientName", out _));
        Assert.False(row.TryGetProperty("appointmentUuid", out _));
    }

    private async Task SeedBillingEventAsync(string providerMessageId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NotificationDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var organization = await db.Organizations
            .SingleAsync(x => x.Key == ProducerWebApplicationFactory.DefaultOrganizationKey);

        db.BillingDeliveryEvents.Add(new BillingDeliveryEventRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Provider = "SwiftSend",
            ReminderType = "24h",
            Status = "Sent",
            OccurredAt = BaseTime,
            CorrelationId = Guid.NewGuid(),
            ProviderMessageId = providerMessageId,
        });

        await db.SaveChangesAsync();
    }
}
