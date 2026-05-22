using System.Text;
using System.Text.Json;
using NotificationModule.Shared.Models;
using RabbitMQ.Client;

namespace NotificationModule.Consumer.Services;

public sealed class RabbitMqRepublisher : IDisposable
{
    private const string ExchangeName = "appointment.notifications";
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqRepublisher> _logger;

    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqRepublisher(IConfiguration config, ILogger<RabbitMqRepublisher> logger)
    {
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? "localhost",
            Port = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = config["RabbitMq:Username"] ?? "guest",
            Password = config["RabbitMq:Password"] ?? "guest",
        };
    }

    public void Initialize()
    {
        lock (_sync)
        {
            EnsureConnected();
        }
    }

    public void Republish(AppointmentMessage message, string targetProvider)
    {
        if (string.IsNullOrWhiteSpace(targetProvider))
            throw new ArgumentException("targetProvider is required.", nameof(targetProvider));

        lock (_sync)
        {
            EnsureConnected();

            var updated = message with { TargetProvider = targetProvider };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated));

            var props = _channel!.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.MessageId = Guid.NewGuid().ToString();
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            _channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: targetProvider.Trim(),
                basicProperties: props,
                body: body);
        }
    }

    private void EnsureConnected()
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
            return;

        _channel?.Dispose();
        _connection?.Dispose();

        _connection = _factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
        _logger.LogInformation("RabbitMqRepublisher connected to {Host}:{Port}", _factory.HostName, _factory.Port);
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();
    }
}

