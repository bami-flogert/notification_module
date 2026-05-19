using System.Text.Json.Serialization;

namespace NotificationModule.Consumer.Secrets;

public static class ProviderSecretKeys
{
    public const string SwiftSend = "SwiftSend";
    public const string SecurePost = "SecurePost";
    public const string LegacyLink = "LegacyLink";
    public const string AsyncFlow = "AsyncFlow";

    public static IReadOnlyList<string> All { get; } =
    [
        SwiftSend,
        SecurePost,
        LegacyLink,
        AsyncFlow,
    ];
}

public sealed class SwiftSendSecretPayload
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";
}

public sealed class SecurePostSecretPayload
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("clientSecret")]
    public string ClientSecret { get; set; } = "";
}

public sealed class LegacyLinkSecretPayload
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

public sealed class AsyncFlowSecretPayload
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";
}
