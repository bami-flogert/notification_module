using System.Text;
using System.Text.Json;
using NotificationModule.Consumer.Services;
using NotificationModule.Shared.Models;
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
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationWorker> _logger;

    private IConnection? _connection;
    private readonly List<IModel> _channels = new();

    public NotificationWorker(
        NotificationDispatcher dispatcher,
        DeliveryTrackingService deliveryTracking,
        IConfiguration config,
        ILogger<NotificationWorker> logger)
    {
        _dispatcher = dispatcher;
        _deliveryTracking = deliveryTracking;
        _config     = config;
        _logger     = logger;
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
                    var json    = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (message is not null)
                    {
                        // Determine which FakeComWorld provider to invoke for this queue.
                        var providerName =
                            queue.Contains("swiftsend", StringComparison.OrdinalIgnoreCase) ? "SwiftSend"
                          : queue.Contains("securepost", StringComparison.OrdinalIgnoreCase) ? "SecurePost"
                          : queue.Contains("legacylink", StringComparison.OrdinalIgnoreCase) ? "LegacyLink"
                          : queue.Contains("asyncflow", StringComparison.OrdinalIgnoreCase) ? "AsyncFlow"
                          : null;

                        if (providerName is not null)
                        {
                            var result = await _dispatcher.DispatchToProviderAsync(
                                message,
                                providerName,
                                stoppingToken);

                            await _deliveryTracking.RecordAsync(
                                message,
                                providerName,
                                result.Success,
                                result.ErrorMessage,
                                stoppingToken);
                        }
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

    private async Task WaitForRabbitMqAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName          = _config["RabbitMq:Host"] ?? "localhost",
            Port              = int.Parse(_config["RabbitMq:Port"] ?? "5672"),
            UserName          = _config["RabbitMq:Username"] ?? "guest",
            Password          = _config["RabbitMq:Password"] ?? "guest",
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

