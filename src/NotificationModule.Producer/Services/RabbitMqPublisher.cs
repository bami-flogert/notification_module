using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NotificationModule.Shared.Models;
using RabbitMQ.Client;

namespace NotificationModule.Producer.Services;

public class RabbitMqPublisher : IDisposable
{
    private const string ExchangeName = "appointment.notifications";
    private static readonly string[] QueueNames =
    {
        "notifications.swiftsend",
        "notifications.securepost",
        "notifications.legacylink",
        "notifications.asyncflow"
    };

    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly ConnectionFactory _factory;
    private readonly object _sync = new();

    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqPublisher(IConfiguration config, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;

        _factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? "localhost",
            Port     = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = config["RabbitMq:Username"] ?? "guest",
            Password = config["RabbitMq:Password"] ?? "guest",
        };
    }

    /// <summary>
    /// Declare the fanout exchange and the three queues (sms, whatsapp, email).
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public void Initialize()
    {
        lock (_sync)
        {
            EnsureConnectedWithRetry();
        }
    }

    public void Publish(AppointmentMessage message)
    {
        lock (_sync)
        {
            EnsureConnectedWithRetry();
            PublishOnOpenChannel(message);
        }
    }

    public void PublishBatch(IEnumerable<AppointmentMessage> messages)
    {
        lock (_sync)
        {
            EnsureConnectedWithRetry();
            foreach (var message in messages)
                PublishOnOpenChannel(message);
        }
    }

    private void PublishOnOpenChannel(AppointmentMessage message)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var props = _channel!.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = Guid.NewGuid().ToString();
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: string.Empty,
            basicProperties: props,
            body: body);
    }

    private void EnsureConnectedWithRetry(int delayMs = 3000)
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
            return;

        while (true)
        {
            try
            {
                _connection?.Dispose();
                _channel?.Dispose();

                _connection = _factory.CreateConnection();
                _channel    = _connection.CreateModel();
                DeclareTopology();
                _logger.LogInformation("Connected to RabbitMQ host {Host}:{Port}", _factory.HostName, _factory.Port);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RabbitMQ unavailable at {Host}:{Port}. Retrying in {DelaySeconds}s.",
                    _factory.HostName,
                    _factory.Port,
                    delayMs / 1000);
                Thread.Sleep(delayMs);
            }
        }
    }

    private void DeclareTopology()
    {
        _channel!.ExchangeDeclare(ExchangeName, ExchangeType.Fanout, durable: true);

        foreach (var queue in QueueNames)
        {
            _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(queue, ExchangeName, routingKey: string.Empty);
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();

        _channel?.Dispose();
        _connection?.Dispose();
    }
}

