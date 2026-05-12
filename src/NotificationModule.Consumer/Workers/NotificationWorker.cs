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

    // This worker can listen to multiple logical queues (sms/whatsapp/email).
    // In a multi-channel setup each channel has its own queue. The worker will
    // create a consumer for each queue and dispatch messages to all providers.

    private static readonly string[] QueueNames =
    {
        "notifications.sms",
        "notifications.whatsapp",
        "notifications.email"
    };

    private readonly NotificationDispatcher _dispatcher;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationWorker> _logger;

    private IConnection? _connection;
    private IModel? _channel;

    public NotificationWorker(
        NotificationDispatcher dispatcher,
        IConfiguration config,
        ILogger<NotificationWorker> logger)
    {
        _dispatcher = dispatcher;
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
            var consumer = new AsyncEventingBasicConsumer(_channel!);

            consumer.Received += async (_, ea) =>
            {
                try
                {
                    var json    = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (message is not null)
                    {
                        // Determine which provider to invoke for this queue.
                        var channel = queue.Contains("sms", StringComparison.OrdinalIgnoreCase) ? "SMS"
                                    : queue.Contains("whatsapp", StringComparison.OrdinalIgnoreCase) ? "WhatsApp"
                                    : queue.Contains("email", StringComparison.OrdinalIgnoreCase) ? "Email"
                                    : null;

                        await _dispatcher.DispatchAsync(message, channel, stoppingToken);
                    }

                    _channel!.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message. Nacking.");
                    _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel!.BasicConsume(queue, autoAck: false, consumer: consumer);
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
                _channel    = _connection.CreateModel();
                DeclareTopology(_channel);
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
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
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

