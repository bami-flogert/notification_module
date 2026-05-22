using System.Text;
using System.Text.Json;
using NotificationModule.Consumer.Adapters;
using NotificationModule.Consumer.Services;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Models;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;

namespace NotificationModule.Consumer.Workers;

public class NotificationWorker : BackgroundService
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
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
    private readonly INotificationProvider[] _providers;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationWorker> _logger;

    private IConnection? _connection;
    private readonly List<IModel> _channels = new();

    public NotificationWorker(
        NotificationDispatcher dispatcher,
        DeliveryTrackingService deliveryTracking,
        IEnumerable<INotificationProvider> providers,
        IConfiguration config,
        ILogger<NotificationWorker> logger)
    {
        _dispatcher = dispatcher;
        _deliveryTracking = deliveryTracking;
        _providers = providers.ToArray();
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
                string? mappedProvider = null;
                try
                {
                    var parentContext = Propagator.Extract(
                        default,
                        ea.BasicProperties,
                        ExtractTraceContextFromBasicProperties);

                    Baggage.Current = parentContext.Baggage;
                    using var activity = NotificationTelemetry.ActivitySource.StartActivity(
                        "rabbitmq.consume.appointment_notification",
                        ActivityKind.Consumer,
                        parentContext.ActivityContext);

                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (message is null)
                    {
                        RecordMessageFailed(queue, null, "deserialize");
                        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    mappedProvider = NotificationQueueMapping.TryGetProviderName(queue);
                    RecordMessageReceived(queue, mappedProvider);

                    activity?.SetTag("messaging.rabbitmq.queue", queue);
                    activity?.SetTag("appointment.uuid", message.AppointmentUuid);
                    activity?.SetTag("organization.key", message.OrganizationKey);
                    activity?.SetTag("scheduled_notification.id", message.ScheduledNotificationId?.ToString());

                    if (mappedProvider is not null)
                    {
                        await DispatchAndTrackAsync(message, queue, mappedProvider, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Queue {Queue} is not mapped to a provider; fan-out to all registered providers.",
                            queue);
                        await DispatchToAllProvidersAsync(message, queue, stoppingToken);
                    }

                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message. Nacking.");
                    RecordMessageFailed(queue, mappedProvider, "exception");
                    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            channel.BasicConsume(queue, autoAck: false, consumer: consumer);
        }

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static IEnumerable<string> ExtractTraceContextFromBasicProperties(IBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return Enumerable.Empty<string>();

        if (value is byte[] bytes)
            return [Encoding.UTF8.GetString(bytes)];

        if (value is string str)
            return [str];

        return Enumerable.Empty<string>();
    }

    private async Task DispatchToAllProvidersAsync(
        AppointmentMessage message,
        string queue,
        CancellationToken stoppingToken)
    {
        foreach (var provider in _providers)
            await DispatchAndTrackAsync(message, queue, provider.ChannelName, stoppingToken);
    }

    private async Task DispatchAndTrackAsync(
        AppointmentMessage message,
        string queue,
        string providerName,
        CancellationToken stoppingToken)
    {
        var result = await _dispatcher.DispatchToProviderAsync(
            message,
            providerName,
            stoppingToken);

        if (!result.Success)
            RecordMessageFailed(queue, providerName, "dispatch");

        await _deliveryTracking.RecordAsync(
            message,
            providerName,
            result.Success,
            result.ErrorMessage,
            stoppingToken,
            result.ErrorType);
    }

    private static void RecordMessageReceived(string queue, string? provider)
    {
        if (provider is not null)
        {
            NotificationTelemetry.NotificationMessagesReceived.Add(
                1,
                new KeyValuePair<string, object?>("queue", queue),
                new KeyValuePair<string, object?>("provider", provider));
            return;
        }

        NotificationTelemetry.NotificationMessagesReceived.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue));
    }

    private static void RecordMessageFailed(string queue, string? provider, string failureReason)
    {
        if (provider is not null)
        {
            NotificationTelemetry.NotificationMessagesFailed.Add(
                1,
                new KeyValuePair<string, object?>("queue", queue),
                new KeyValuePair<string, object?>("provider", provider),
                new KeyValuePair<string, object?>("failure_reason", failureReason));
            return;
        }

        NotificationTelemetry.NotificationMessagesFailed.Add(
            1,
            new KeyValuePair<string, object?>("queue", queue),
            new KeyValuePair<string, object?>("failure_reason", failureReason));
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
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Fanout, durable: true);

        // Ensure all logical queues exist and are bound to the fanout exchange.
        foreach (var queue in QueueNames)
        {
            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(queue, ExchangeName, routingKey: string.Empty);
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
