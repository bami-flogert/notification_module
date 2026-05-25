using NotificationModule.Shared.Messaging;
using RabbitMQ.Client;

namespace NotificationModule.Consumer.Messaging;

public sealed class RabbitMqDeadLetterPublisher
{
    public void PublishToDeadLetterQueue(
        IModel channel,
        string mainQueue,
        byte[] body,
        IBasicProperties sourceProps,
        string reason)
    {
        var dlq = RabbitMqTopology.GetDeadLetterQueueName(mainQueue);
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = sourceProps.ContentType ?? "application/json";
        props.MessageId = sourceProps.MessageId ?? Guid.NewGuid().ToString();
        props.Timestamp = sourceProps.Timestamp;
        props.Headers = CopyHeaders(sourceProps.Headers);
        props.Headers[RabbitMqTopology.DlqReasonHeader] = reason;

        channel.BasicPublish(
            exchange: string.Empty,
            routingKey: dlq,
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    public void RepublishForRetry(
        IModel channel,
        string mainQueue,
        byte[] body,
        IBasicProperties sourceProps,
        int nextRetryCount)
    {
        var routingKey = RabbitMqTopology.TryGetRoutingKey(mainQueue)
            ?? throw new InvalidOperationException($"Queue '{mainQueue}' has no routing key.");

        var props = RabbitMqMessageFailurePolicy.BuildRetryProperties(channel, sourceProps, nextRetryCount);

        channel.BasicPublish(
            exchange: RabbitMqTopology.ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    private static IDictionary<string, object?> CopyHeaders(IDictionary<string, object>? headers)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (headers is null)
            return copy;

        foreach (var (key, value) in headers)
            copy[key] = value;

        return copy;
    }
}
