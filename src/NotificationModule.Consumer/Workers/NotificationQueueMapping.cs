namespace NotificationModule.Consumer.Workers;

public static class NotificationQueueMapping
{
    public static string? TryGetProviderName(string queueName)
    {
        if (queueName.Contains("swiftsend", StringComparison.OrdinalIgnoreCase))
            return "SwiftSend";
        if (queueName.Contains("securepost", StringComparison.OrdinalIgnoreCase))
            return "SecurePost";
        if (queueName.Contains("legacylink", StringComparison.OrdinalIgnoreCase))
            return "LegacyLink";
        if (queueName.Contains("asyncflow", StringComparison.OrdinalIgnoreCase))
            return "AsyncFlow";
        return null;
    }
}
