using Moq;
using NotificationModule.Consumer.Messaging;
using NotificationModule.Shared.Messaging;
using RabbitMQ.Client;

namespace NotificationModule.Tests.Consumer;

public sealed class RabbitMqDeadLetterPublisherTests
{
    [Fact]
    public void PublishToDeadLetterQueue_routes_to_dlq_with_reason_header()
    {
        var capture = new PublishCapture();
        var channel = CreateChannelMock(capture);
        var source = new Mock<IBasicProperties>();
        source.SetupProperty(p => p.ContentType, "application/json");
        source.SetupProperty(p => p.Headers, new Dictionary<string, object>());

        var publisher = new RabbitMqDeadLetterPublisher();
        publisher.PublishToDeadLetterQueue(
            channel.Object,
            "notifications.swiftsend",
            [1, 2, 3],
            source.Object,
            "deserialize");

        Assert.Equal(string.Empty, capture.Exchange);
        Assert.Equal("notifications.swiftsend.dlq", capture.RoutingKey);
        Assert.NotNull(capture.Properties);
        Assert.Equal("deserialize", capture.Properties!.Headers![RabbitMqTopology.DlqReasonHeader]);
        Assert.True(capture.Properties.Persistent);
    }

    [Fact]
    public void RepublishForRetry_publishes_to_exchange_with_provider_routing_key()
    {
        var capture = new PublishCapture();
        var channel = CreateChannelMock(capture);

        var publisher = new RabbitMqDeadLetterPublisher();
        var source = new Mock<IBasicProperties>();
        source.SetupProperty(p => p.Headers, new Dictionary<string, object>());

        publisher.RepublishForRetry(
            channel.Object,
            "notifications.swiftsend",
            [0x7B, 0x7D],
            source.Object,
            nextRetryCount: 1);

        Assert.Equal(RabbitMqTopology.ExchangeName, capture.Exchange);
        Assert.Equal("SwiftSend", capture.RoutingKey);
        Assert.Equal(1, capture.Properties!.Headers![RabbitMqTopology.RetryCountHeader]);
    }

    private static Mock<IModel> CreateChannelMock(PublishCapture capture)
    {
        var channel = new Mock<IModel>();
        var created = new Mock<IBasicProperties>();
        created.SetupProperty(p => p.Headers, new Dictionary<string, object>());
        created.SetupProperty(p => p.Persistent);
        created.SetupProperty(p => p.ContentType);
        created.SetupProperty(p => p.MessageId);
        created.SetupProperty(p => p.Timestamp);
        channel.Setup(c => c.CreateBasicProperties()).Returns(created.Object);
        channel
            .Setup(c => c.BasicPublish(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<IBasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>()))
            .Callback<string, string, bool, IBasicProperties, ReadOnlyMemory<byte>>(
                (ex, rk, _, props, _) =>
                {
                    capture.Exchange = ex;
                    capture.RoutingKey = rk;
                    capture.Properties = props;
                });

        return channel;
    }

    private sealed class PublishCapture
    {
        public string? Exchange { get; set; }
        public string? RoutingKey { get; set; }
        public IBasicProperties? Properties { get; set; }
    }
}
