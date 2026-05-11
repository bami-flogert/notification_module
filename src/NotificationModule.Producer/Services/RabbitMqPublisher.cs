using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NotificationModule.Shared.Models;
using RabbitMQ.Client;

namespace NotificationModule.Producer.Services;

public class RabbitMqPublisher : IDisposable
{
    private const string ExchangeName = "appointment.notifications";

    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher(IConfiguration config)
    {
        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? "localhost",
            Port     = int.Parse(config["RabbitMq:Port"] ?? "5672"),
            UserName = config["RabbitMq:Username"] ?? "guest",
            Password = config["RabbitMq:Password"] ?? "guest",
        };

        _connection = factory.CreateConnection();
        _channel    = _connection.CreateModel();
    }

    /// <summary>
    /// Declare the fanout exchange and the three queues (sms, whatsapp, email).
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public void Initialize()
    {
        _channel.ExchangeDeclare(ExchangeName, ExchangeType.Fanout, durable: true);

        foreach (var queue in new[] { "notifications.sms", "notifications.whatsapp", "notifications.email" })
        {
            _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(queue, ExchangeName, routingKey: string.Empty);
        }
    }

    public void Publish(AppointmentMessage message)
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var props = _channel.CreateBasicProperties();
        props.Persistent   = true;           // survive broker restart
        props.ContentType  = "application/json";
        props.MessageId    = Guid.NewGuid().ToString();
        props.Timestamp    = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        _channel.BasicPublish(
            exchange:   ExchangeName,
            routingKey: string.Empty,
            basicProperties: props,
            body:       body);
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
    }
}

