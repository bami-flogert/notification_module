using System.Text.Json;
using System.Xml.Linq;

namespace NotificationModule.Consumer.Adapters;

/// <summary>
/// Parses external provider message/tracking IDs from FakeComWorld HTTP response bodies.
/// SwiftSend: JSON <c>messageId</c>; SecurePost: JSON <c>trackingId</c>;
/// LegacyLink: XML <c>MessageReference</c>; AsyncFlow: JSON <c>trackingId</c>.
/// </summary>
public static class ProviderResponseIds
{
    public static string? TryParseSwiftSendMessageId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("messageId", out var el)
                ? NullIfWhiteSpace(el.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? TryParseSecurePostTrackingId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("trackingId", out var el)
                ? NullIfWhiteSpace(el.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? TryParseLegacyLinkMessageReference(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var document = XDocument.Parse(xml);
            var reference = document.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "MessageReference");

            return NullIfWhiteSpace(reference?.Value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? TryParseAsyncFlowTrackingId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("trackingId", out var el)
                ? NullIfWhiteSpace(el.GetString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
