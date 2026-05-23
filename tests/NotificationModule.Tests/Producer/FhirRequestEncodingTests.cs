using System.Text;
using NotificationModule.Producer.Fhir;

namespace NotificationModule.Tests.Producer;

public sealed class FhirRequestEncodingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/fhir+json")]
    [InlineData("application/fhir+json; charset=utf-8")]
    [InlineData("application/fhir+json; charset=UTF-8")]
    public void IsSupportedContentType_accepts_utf8_or_missing_charset(string? contentType)
    {
        Assert.True(FhirRequestEncoding.IsSupportedContentType(contentType, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("application/fhir+json; charset=iso-8859-1")]
    [InlineData("application/fhir+json; charset=windows-1252")]
    public void IsSupportedContentType_rejects_non_utf8_charset(string contentType)
    {
        Assert.False(FhirRequestEncoding.IsSupportedContentType(contentType, out var error));
        Assert.Equal(FhirRequestEncoding.UnsupportedCharsetMessage, error);
    }

    [Fact]
    public void TryDecodeUtf8Body_accepts_valid_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"resourceType\":\"Appointment\"}");
        Assert.True(FhirRequestEncoding.TryDecodeUtf8Body(bytes, out var text, out var error));
        Assert.Null(error);
        Assert.Contains("Appointment", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDecodeUtf8Body_rejects_invalid_utf8_sequence()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x00 };
        Assert.False(FhirRequestEncoding.TryDecodeUtf8Body(bytes, out _, out var error));
        Assert.Equal(FhirRequestEncoding.InvalidUtf8BodyMessage, error);
    }
}
