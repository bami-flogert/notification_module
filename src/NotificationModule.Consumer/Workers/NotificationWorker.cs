using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Consumer.Services;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationModule.Consumer.Workers;

public class NotificationWorker : BackgroundService
{
    private const string ExchangeName = "appointment.notifications";

    // This worker can listen to multiple provider queues (SwiftSend/SecurePost/LegacyLink/AsyncFlow).
    // In a multi-channel setup each channel has its own queue. The worker will
    // create a consumer for each queue and dispatch messages to all providers.

    private static readonly string[] QueueNames =
    {
        "notifications.swiftsend",
        "notifications.securepost",
        "notifications.legacylink",
        "notifications.asyncflow"
    };

    private readonly NotificationDispatcher _dispatcher;
    private readonly DeliveryTrackingService _deliveryTracking;
    private readonly RabbitMqRepublisher _republisher;
    private readonly IDbContextFactory<SecretsDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationWorker> _logger;

    private IConnection? _connection;
    private readonly List<IModel> _channels = new();

    public NotificationWorker(
        NotificationDispatcher dispatcher,
        DeliveryTrackingService deliveryTracking,
        RabbitMqRepublisher republisher,
        IDbContextFactory<SecretsDbContext> dbFactory,
        IConfiguration config,
        ILogger<NotificationWorker> logger)
    {
        _dispatcher = dispatcher;
        _deliveryTracking = deliveryTracking;
        _republisher = republisher;
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for RabbitMQ to be ready (simple retry loop)
        await WaitForRabbitMqAsync(stoppingToken);

        // Create one consumer per queue so each logical queue can be consumed
        // concurrently. Each consumer uses the same dispatch logic.
        foreach (var queue in QueueNames)
        {
            // RabbitMQ .NET channels (IModel) are not thread-safe; use one channel per consumer.
            var channel = _connection!.CreateModel();
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
            _channels.Add(channel);

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.Received += async (_, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (message is not null)
                    {
                        var providerName = NotificationQueueMapping.TryGetProviderName(queue);

                        if (providerName is null)
                            throw new InvalidOperationException($"Queue '{queue}' is not mapped to a provider.");

                        await DispatchAndTrackAsync(message, providerName, stoppingToken);
                    }

                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message. Nacking.");
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicConsume(queue, autoAck: false, consumer: consumer);
        }

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task DispatchAndTrackAsync(
        AppointmentMessage message,
        string providerName,
        CancellationToken stoppingToken)
    {
        var normalized = EnsureAttemptRecorded(message, providerName);
        var result = await _dispatcher.DispatchToProviderAsync(
            normalized,
            providerName,
            stoppingToken);

        await _deliveryTracking.RecordAsync(
            normalized,
            providerName,
            result.Success,
            result.ErrorMessage,
            stoppingToken);

        if (!result.Success)
            await TryRepublishToFallbackAsync(normalized, providerName, stoppingToken);
    }

    private static AppointmentMessage EnsureAttemptRecorded(AppointmentMessage message, string providerName)
    {
        if (!string.IsNullOrWhiteSpace(message.TargetProvider))
            return message;

        return message with { TargetProvider = providerName };
    }

    private async Task TryRepublishToFallbackAsync(
        AppointmentMessage message,
        string failedProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.OrganizationKey))
            return;

        var tried = ParseProviders(message.TriedProviders);
        tried.Add(failedProvider);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var organization = await db.Organizations
            .SingleOrDefaultAsync(x => x.Key == message.OrganizationKey, ct);
        if (organization is null)
            return;

        var chain = BuildProviderChain(organization);
        var next = chain.FirstOrDefault(p => !tried.Contains(p));
        if (string.IsNullOrWhiteSpace(next))
            return;

        var updated = message with { TriedProviders = string.Join(",", tried) };
        _logger.LogWarning(
            "Provider {FailedProvider} failed for scheduled notification {ScheduledNotificationId}; falling back to {NextProvider}.",
            failedProvider,
            message.ScheduledNotificationId,
            next);

        _republisher.Republish(updated, next);
    }

    private static HashSet<string> ParseProviders(string? value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return set;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part);

        return set;
    }

    private static string[] BuildProviderChain(OrganizationRecord organization)
    {
        var providers = new List<string>();
        if (!string.IsNullOrWhiteSpace(organization.PreferredProvider))
            providers.Add(organization.PreferredProvider.Trim());

        if (!string.IsNullOrWhiteSpace(organization.FallbackProviders))
        {
            providers.AddRange(organization.FallbackProviders
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return providers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task WaitForRabbitMqAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMq:Host"] ?? "localhost",
            Port = int.Parse(_config["RabbitMq:Port"] ?? "5672"),
            UserName = _config["RabbitMq:Username"] ?? "guest",
            Password = _config["RabbitMq:Password"] ?? "guest",
            DispatchConsumersAsync = true,
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = factory.CreateConnection();
                using var topologyChannel = _connection.CreateModel();
                DeclareTopology(topologyChannel);
                _logger.LogInformation("Connected to RabbitMQ.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ not ready: {Msg}. Retrying in 3s…", ex.Message);
                await Task.Delay(3000, ct);
            }
        }
    }

    private static void DeclareTopology(IModel channel)
    {
        // Idempotent declarations prevent startup ordering issues between producer and consumer.
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);

        // Ensure all logical queues exist and are bound to the direct exchange.
        foreach (var queue in QueueNames)
        {
            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            var providerName = NotificationQueueMapping.TryGetProviderName(queue)
                ?? throw new InvalidOperationException($"Queue '{queue}' is not mapped to a provider.");
            channel.QueueBind(queue, ExchangeName, routingKey: providerName);
        }
    }

    public override void Dispose()
    {
        foreach (var ch in _channels)
        {
            try { ch.Close(); } catch { /* ignore */ }
            try { ch.Dispose(); } catch { /* ignore */ }
        }
        _connection?.Close();
        base.Dispose();
    }
}
