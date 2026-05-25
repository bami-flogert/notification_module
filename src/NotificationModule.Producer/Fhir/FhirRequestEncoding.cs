using System.Net.Http.Headers;
using System.Text;

namespace NotificationModule.Producer.Fhir;

public static class FhirRequestEncoding
{
    public const string UnsupportedCharsetMessage =
        "Only UTF-8 is supported. Set Content-Type to application/fhir+json or application/fhir+json; charset=utf-8.";

    public const string InvalidUtf8BodyMessage =
        "Request body is not valid UTF-8. Save and send the payload as UTF-8.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsSupportedContentType(string? contentType, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            return true;

        if (string.IsNullOrWhiteSpace(mediaType.CharSet))
            return true;

        if (string.Equals(mediaType.CharSet, "utf-8", StringComparison.OrdinalIgnoreCase))
            return true;

        errorMessage = UnsupportedCharsetMessage;
        return false;
    }

    public static bool TryDecodeUtf8Body(ReadOnlySpan<byte> body, out string text, out string? errorMessage)
    {
        text = string.Empty;
        errorMessage = null;

        if (body.IsEmpty)
        {
            text = string.Empty;
            return true;
        }

        try
        {
            text = StrictUtf8.GetString(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            errorMessage = InvalidUtf8BodyMessage;
            return false;
        }
    }
}
