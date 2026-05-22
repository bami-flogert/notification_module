namespace NotificationModule.Shared.Observability;

/// <summary>
/// Low-cardinality <c>error_type</c> tag values for delivery failure metrics.
/// </summary>
public static class DeliveryErrorTypes
{
    public const string Unknown = "unknown";
    public const string Timeout = "timeout";
    public const string Http4xx = "http_4xx";
    public const string Http5xx = "http_5xx";
    public const string Other = "other";
}
