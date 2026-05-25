namespace NotificationModule.Shared.Messaging;

public static class NotificationProviders
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

    public static bool IsSupported(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && All.Contains(name.Trim(), StringComparer.Ordinal);

    public static void ValidateOrThrow(string name, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"{fieldName} is required.", fieldName);

        if (!IsSupported(name))
            throw new ArgumentException(
                $"Unsupported provider '{name.Trim()}'. Supported values: {string.Join(", ", All)}.",
                fieldName);
    }

    public static string? NormalizeFallbackList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;

        var providers = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (providers.Count == 0)
            return null;

        foreach (var provider in providers)
            ValidateOrThrow(provider, nameof(csv));

        return string.Join(",", providers);
    }
}
