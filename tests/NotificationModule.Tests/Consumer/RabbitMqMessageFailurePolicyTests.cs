using NotificationModule.Consumer.Messaging;
using NotificationModule.Shared.Messaging;

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
}
