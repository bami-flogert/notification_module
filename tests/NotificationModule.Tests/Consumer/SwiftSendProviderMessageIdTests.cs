using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Tests.Consumer;

public sealed class SwiftSendProviderMessageIdTests
{
    private const string ExpectedMessageId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    [Fact]
    public async Task SendAsync_returns_messageId_from_swiftsend_response()
    {
        var provider = await CreateProviderAsync();

        var messageId = await provider.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.Equal(ExpectedMessageId, messageId);
    }

    [Fact]
    public async Task DispatchToProviderAsync_propagates_provider_message_id()
    {
        var provider = await CreateProviderAsync();
        var dispatcher = new NotificationDispatcher(
            [provider],
            NullLogger<NotificationDispatcher>.Instance);

        var result = await dispatcher.DispatchToProviderAsync(
            CreateMessage(),
            "SwiftSend",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ExpectedMessageId, result.ProviderMessageId);
    }

    [Fact]
    public async Task RecordAsync_persists_message_id_from_dispatch_result()
    {
        var provider = await CreateProviderAsync();
        var dispatcher = new NotificationDispatcher(
            [provider],
            NullLogger<NotificationDispatcher>.Instance);
        var (dbFactory, scheduledNotificationId) = await SeedScheduledNotificationAsync();
        var tracking = new DeliveryTrackingService(
            dbFactory,
            NullLogger<DeliveryTrackingService>.Instance);
        var message = CreateMessage(scheduledNotificationId);

        var result = await dispatcher.DispatchToProviderAsync(message, "SwiftSend", CancellationToken.None);
        await tracking.RecordAsync(
            message,
            "SwiftSend",
            result.Success,
            result.ErrorMessage,
            CancellationToken.None,
            result.ErrorType,
            result.ProviderMessageId);

        await using var db = await dbFactory.CreateDbContextAsync();
        var delivery = db.NotificationDeliveries.Single();
        var billing = db.BillingDeliveryEvents.Single();

        Assert.Equal(ExpectedMessageId, delivery.ProviderMessageId);
        Assert.Equal(ExpectedMessageId, billing.ProviderMessageId);
    }

    private static async Task<SwiftSendProvider> CreateProviderAsync()
    {
        var config = TestDb.CreateConfiguration(c =>
        {
            c["Providers:SwiftSend:BaseUrl"] = "http://fake-swiftsend.test/";
            c["Providers:StudentGroup"] = "test-group";
        });

        var protector = new AesGcmSecretProtector(config);
        var dbFactory = TestDb.CreateSecretsFactory();
        await TestDb.SeedOrganizationSecretsAsync(dbFactory, protector, "default", swiftApiKey: "swift-key");

        var secrets = new ProviderSecretsStore(
            dbFactory,
            protector,
            config,
            NullLogger<ProviderSecretsStore>.Instance);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    success = true,
                    messageId = ExpectedMessageId,
                    failedRecipients = Array.Empty<string>(),
                    error = (string?)null,
                }),
                Encoding.UTF8,
                "application/json"),
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake-swiftsend.test/") };
        return new SwiftSendProvider(secrets, config, NullLogger<SwiftSendProvider>.Instance, http);
    }

    private static AppointmentMessage CreateMessage(Guid? scheduledNotificationId = null) => new()
    {
        AppointmentUuid = "appt-1",
        OrganizationKey = "default",
        PatientUuid = "patient-1",
        PatientName = "Test",
        PatientPhone = "+31600000000",
        PatientEmail = "test@example.com",
        StartDateTime = DateTime.UtcNow.AddHours(2),
        Status = "Confirmed",
        ScheduledNotificationId = scheduledNotificationId ?? Guid.NewGuid(),
        ReminderType = "1h",
    };

    private static async Task<(IDbContextFactory<SecretsDbContext> Factory, Guid ScheduledNotificationId)>
        SeedScheduledNotificationAsync()
    {
        var dbFactory = TestDb.CreateSecretsFactory();
        var now = DateTimeOffset.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync();
        var organization = new OrganizationRecord
        {
            Id = Guid.NewGuid(),
            Key = "default",
            Name = "Default",
            TimeZone = "UTC",
            IsEnabled = true,
            PreferredProvider = "SwiftSend",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var appointment = new AppointmentRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            AppointmentUuid = "appt-1",
            PatientUuid = "patient-1",
            StartDateTime = now.AddHours(2),
            Status = "Confirmed",
            SourceSystem = "test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var scheduledNotification = new ScheduledNotificationRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            AppointmentId = appointment.Id,
            ReminderType = "1h",
            ScheduledSendAt = now.AddMinutes(-1),
            Status = ScheduledNotificationStatuses.Publishing,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Organizations.Add(organization);
        db.Appointments.Add(appointment);
        db.ScheduledNotifications.Add(scheduledNotification);
        await db.SaveChangesAsync();

        return (dbFactory, scheduledNotification.Id);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
