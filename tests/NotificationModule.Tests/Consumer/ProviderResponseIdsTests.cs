using NotificationModule.Consumer.Adapters;

namespace NotificationModule.Tests.Consumer;

public sealed class ProviderResponseIdsTests
{
    [Fact]
    public void TryParseSwiftSendMessageId_returns_messageId_from_success_response()
    {
        const string json = """
            {
              "success": true,
              "messageId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
              "failedRecipients": [],
              "error": null
            }
            """;

        var result = ProviderResponseIds.TryParseSwiftSendMessageId(json);

        Assert.Equal("a1b2c3d4-e5f6-7890-abcd-ef1234567890", result);
    }

    [Fact]
    public void TryParseSwiftSendMessageId_returns_null_when_messageId_missing()
    {
        const string json = """{ "success": true, "failedRecipients": [] }""";

        Assert.Null(ProviderResponseIds.TryParseSwiftSendMessageId(json));
    }

    [Fact]
    public void TryParseSecurePostTrackingId_returns_trackingId_from_success_response()
    {
        const string json = """
            {
              "delivered": true,
              "trackingId": "A1B2C3D4E5F67890ABCDEF1234567890",
              "errorMessage": null,
              "deliveryTimestamp": "2024-01-15T10:35:42Z"
            }
            """;

        var result = ProviderResponseIds.TryParseSecurePostTrackingId(json);

        Assert.Equal("A1B2C3D4E5F67890ABCDEF1234567890", result);
    }

    [Fact]
    public void TryParseSecurePostTrackingId_returns_null_when_trackingId_missing()
    {
        const string json = """{ "delivered": true, "errorMessage": null }""";

        Assert.Null(ProviderResponseIds.TryParseSecurePostTrackingId(json));
    }

    [Fact]
    public void TryParseLegacyLinkMessageReference_returns_message_reference_from_success_response()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <SendSmsResponse xmlns="http://legacylink.fakecomworld.com/v1">
              <StatusCode>200</StatusCode>
              <StatusMessage>SMS sent successfully</StatusMessage>
              <MessageReference>LGC-A1B2C3D4E5F67890</MessageReference>
              <Timestamp>2024-01-15T10:35:42Z</Timestamp>
            </SendSmsResponse>
            """;

        var result = ProviderResponseIds.TryParseLegacyLinkMessageReference(xml);

        Assert.Equal("LGC-A1B2C3D4E5F67890", result);
    }

    [Fact]
    public void TryParseLegacyLinkMessageReference_returns_null_when_message_reference_empty()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <SendSmsResponse xmlns="http://legacylink.fakecomworld.com/v1">
              <StatusCode>401</StatusCode>
              <StatusMessage>Authentication required.</StatusMessage>
              <MessageReference></MessageReference>
              <Timestamp>2024-01-15T10:30:00Z</Timestamp>
            </SendSmsResponse>
            """;

        Assert.Null(ProviderResponseIds.TryParseLegacyLinkMessageReference(xml));
    }

    [Fact]
    public void TryParseAsyncFlowTrackingId_returns_trackingId_from_submit_response()
    {
        const string json = """
            {
              "accepted": true,
              "trackingId": "ASF-A1B2C3D4E5F67890ABCDEF1234567890",
              "message": "Message queued for processing",
              "submittedAt": "2024-01-15T10:30:00Z"
            }
            """;

        var result = ProviderResponseIds.TryParseAsyncFlowTrackingId(json);

        Assert.Equal("ASF-A1B2C3D4E5F67890ABCDEF1234567890", result);
    }
}
