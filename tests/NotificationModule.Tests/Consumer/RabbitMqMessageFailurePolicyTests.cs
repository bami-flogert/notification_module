using Moq;
using NotificationModule.Consumer.Messaging;
using NotificationModule.Shared.Messaging;
using RabbitMQ.Client;

namespace NotificationModule.Tests.Consumer;

public sealed class RabbitMqMessageFailurePolicyTests
{
    [Fact]
    public void ShouldDeadLetterImmediately_returns_true_for_deserialize() =>
        Assert.True(RabbitMqMessageFailurePolicy.ShouldDeadLetterImmediately(MessageFailureKind.Deserialize));

    [Fact]
    public void ShouldDeadLetterImmediately_returns_false_for_processing_exception() =>
        Assert.False(RabbitMqMessageFailurePolicy.ShouldDeadLetterImmediately(MessageFailureKind.ProcessingException));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ShouldDeadLetterAfterRetry_respects_max_three_deliveries(int retryCount, bool expected) =>
        Assert.Equal(expected, RabbitMqMessageFailurePolicy.ShouldDeadLetterAfterRetry(retryCount));

    [Fact]
    public void GetDeadLetterQueueName_appends_dlq_suffix() =>
        Assert.Equal(
            "notifications.swiftsend.dlq",
            RabbitMqTopology.GetDeadLetterQueueName("notifications.swiftsend"));

    [Fact]
    public void GetRetryCountFromHeaders_reads_x_retry_count()
    {
        var headers = new Dictionary<string, object>
        {
            [RabbitMqTopology.RetryCountHeader] = 2,
        };

        Assert.Equal(2, RabbitMqMessageFailurePolicy.GetRetryCountFromHeaders(headers));
    }

    [Fact]
    public void GetRetryCountFromHeaders_returns_zero_when_header_missing() =>
        Assert.Equal(0, RabbitMqMessageFailurePolicy.GetRetryCountFromHeaders(null));

    [Fact]
    public void BuildRetryProperties_sets_x_retry_count_on_republished_message()
    {
        var created = new Mock<IBasicProperties>();
        created.SetupProperty(p => p.Headers, new Dictionary<string, object>());
        created.SetupProperty(p => p.Persistent);
        created.SetupProperty(p => p.ContentType);

        var channel = new Mock<IModel>();
        channel.Setup(c => c.CreateBasicProperties()).Returns(created.Object);

        var source = new Mock<IBasicProperties>();
        source.SetupProperty(p => p.Persistent, true);
        source.SetupProperty(p => p.ContentType, "application/json");
        source.SetupProperty(p => p.Headers, new Dictionary<string, object>
        {
            [RabbitMqTopology.RetryCountHeader] = 0,
        });

        var result = RabbitMqMessageFailurePolicy.BuildRetryProperties(
            channel.Object,
            source.Object,
            nextRetryCount: 1);

        Assert.Same(created.Object, result);
        Assert.True(result.Persistent);
        Assert.Equal("application/json", result.ContentType);
        Assert.Equal(1, result.Headers[RabbitMqTopology.RetryCountHeader]);
    }

    [Theory]
    [InlineData(MessageFailureKind.Deserialize, 0, true)]
    [InlineData(MessageFailureKind.Deserialize, 2, true)]
    [InlineData(MessageFailureKind.ProcessingException, 0, false)]
    [InlineData(MessageFailureKind.ProcessingException, 1, false)]
    [InlineData(MessageFailureKind.ProcessingException, 2, true)]
    public void Failure_routing_deserialize_immediate_dlq_processing_retries_then_dlq(
        MessageFailureKind kind,
        int retryCount,
        bool expectDlq)
    {
        var toDlq = RabbitMqMessageFailurePolicy.ShouldDeadLetterImmediately(kind)
            || RabbitMqMessageFailurePolicy.ShouldDeadLetterAfterRetry(retryCount);

        Assert.Equal(expectDlq, toDlq);
    }
}
