namespace NotificationModule.Shared.Observability;

public static class DeliveryErrorClassifier
{
    public static string Classify(Exception? exception)
    {
        if (exception is null)
            return DeliveryErrorTypes.Unknown;

        string? fallback = null;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var classified = ClassifySingle(current);
            if (classified is DeliveryErrorTypes.Timeout
                or DeliveryErrorTypes.Http4xx
                or DeliveryErrorTypes.Http5xx)
                return classified;

            if (classified == DeliveryErrorTypes.Other)
                fallback = DeliveryErrorTypes.Other;
        }

        return fallback ?? DeliveryErrorTypes.Unknown;
    }

    private static string ClassifySingle(Exception exception) => exception switch
    {
        TimeoutException => DeliveryErrorTypes.Timeout,
        TaskCanceledException => ClassifyTaskCanceled(exception),
        OperationCanceledException => ClassifyOperationCanceled(exception),
        HttpRequestException http => ClassifyHttp(http),
        _ => DeliveryErrorTypes.Other,
    };

    private static string ClassifyTaskCanceled(Exception exception)
    {
        if (exception.InnerException is TimeoutException)
            return DeliveryErrorTypes.Timeout;

        return ContainsTimeoutHint(exception.Message)
            ? DeliveryErrorTypes.Timeout
            : DeliveryErrorTypes.Other;
    }

    private static string ClassifyOperationCanceled(Exception exception)
    {
        if (exception.InnerException is TimeoutException)
            return DeliveryErrorTypes.Timeout;

        return ContainsTimeoutHint(exception.Message)
            ? DeliveryErrorTypes.Timeout
            : DeliveryErrorTypes.Other;
    }

    private static string ClassifyHttp(HttpRequestException exception)
    {
        var statusCode = exception.StatusCode;
        if (statusCode is null)
            return DeliveryErrorTypes.Other;

        var code = (int)statusCode.Value;
        if (code is >= 400 and < 500)
            return DeliveryErrorTypes.Http4xx;
        if (code >= 500)
            return DeliveryErrorTypes.Http5xx;

        return DeliveryErrorTypes.Other;
    }

    private static bool ContainsTimeoutHint(string? message) =>
        !string.IsNullOrEmpty(message)
        && message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
}
