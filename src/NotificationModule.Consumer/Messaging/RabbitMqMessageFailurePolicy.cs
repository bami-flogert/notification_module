using NotificationModule.Shared.Messaging;
using RabbitMQ.Client;

namespace NotificationModule.Consumer.Messaging;

public enum MessageFailureKind
{
    Deserialize,
    ProcessingException,
}

public static class RabbitMqMessageFailurePolicy
{
    public const int MaxDeliveryAttempts = 3;

    public static int GetRetryCount(IBasicProperties? properties) =>
        GetRetryCountFromHeaders(properties?.Headers);

    public static int GetRetryCountFromHeaders(IDictionary<string, object>? headers) =>
        ReadHeaderInt(headers, RabbitMqTopology.RetryCountHeader);

    public static bool ShouldDeadLetterImmediately(MessageFailureKind kind) =>
        kind == MessageFailureKind.Deserialize;

    public static bool ShouldDeadLetterAfterRetry(int retryCount) =>
        retryCount >= MaxDeliveryAttempts - 1;

    public static int GetNextRetryCount(int currentRetryCount) => currentRetryCount + 1;

    public static IBasicProperties BuildRetryProperties(
        IModel channel,
        IBasicProperties source,
        int nextRetryCount)
    {
        var props = channel.CreateBasicProperties();
        props.Persistent = source.Persistent;
        props.ContentType = source.ContentType;
        props.MessageId = source.MessageId ?? Guid.NewGuid().ToString();
        props.Timestamp = source.Timestamp;
        props.Headers = CopyHeaders(source.Headers);
        props.Headers[RabbitMqTopology.RetryCountHeader] = nextRetryCount;
        return props;
    }

    private static IDictionary<string, object?> CopyHeaders(IDictionary<string, object>? headers)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (headers is null)
            return copy;

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, RabbitMqTopology.DlqReasonHeader, StringComparison.OrdinalIgnoreCase))
                continue;

            copy[key] = value;
        }

        return copy;
    }

    private static int ReadHeaderInt(IDictionary<string, object>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null)
            return 0;

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when bytes.Length <= 4 => BitConverter.ToInt32(bytes, 0),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }
}
