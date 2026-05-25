using RabbitMQ.Client;

namespace NotificationModule.Shared.Messaging;

public static class RabbitMqTopology
{
    public const string ExchangeName = "appointment.notifications";
    public const string RetryCountHeader = "x-retry-count";
    public const string DlqReasonHeader = "x-dlq-reason";

    public static readonly (string Queue, string RoutingKey)[] QueueBindings =
    [
        ("notifications.swiftsend", "SwiftSend"),
        ("notifications.securepost", "SecurePost"),
        ("notifications.legacylink", "LegacyLink"),
        ("notifications.asyncflow", "AsyncFlow"),
    ];

    public static string GetDeadLetterQueueName(string mainQueueName) => $"{mainQueueName}.dlq";

    public static string? TryGetRoutingKey(string mainQueueName)
    {
        foreach (var (queue, routingKey) in QueueBindings)
        {
            if (string.Equals(queue, mainQueueName, StringComparison.OrdinalIgnoreCase))
                return routingKey;
        }

        return null;
    }

    public static void Declare(IModel channel)
    {
        channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);

        foreach (var (queue, routingKey) in QueueBindings)
        {
            channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(queue, ExchangeName, routingKey: routingKey);

            var dlq = GetDeadLetterQueueName(queue);
            channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        }
    }
}
