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

    // This worker listens to a single logical queue.
    // In a multi-channel setup each channel has its own queue (sms/whatsapp/email).
    // For simplicity this worker picks from all three, dispatching to all providers.
    // You can split into three separate workers if needed.

    private const string QueueName = "notifications.sms"; // swap or loop for all queues

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

        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json    = Encoding.UTF8.GetString(ea.Body.ToArray());
                var message = JsonSerializer.Deserialize<AppointmentMessage>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (message is not null)
                    await _dispatcher.DispatchAsync(message, stoppingToken);

                _channel!.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message. Nacking.");
                _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel!.BasicConsume(QueueName, autoAck: false, consumer: consumer);

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
        channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(QueueName, ExchangeName, routingKey: string.Empty);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}

