using System.Net;
using NotificationModule.Shared.Observability;

namespace NotificationModule.Tests.Observability;

public sealed class DeliveryErrorClassifierTests
{
    [Fact]
    public void Classify_null_returns_unknown() =>
        Assert.Equal(DeliveryErrorTypes.Unknown, DeliveryErrorClassifier.Classify(null));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DeliveryErrorTypes.Http4xx)]
    [InlineData(HttpStatusCode.Unauthorized, DeliveryErrorTypes.Http4xx)]
    [InlineData(HttpStatusCode.NotFound, DeliveryErrorTypes.Http4xx)]
    [InlineData(HttpStatusCode.InternalServerError, DeliveryErrorTypes.Http5xx)]
    [InlineData(HttpStatusCode.BadGateway, DeliveryErrorTypes.Http5xx)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DeliveryErrorTypes.Http5xx)]
    public void Classify_http_status_codes(HttpStatusCode statusCode, string expected) =>
        Assert.Equal(
            expected,
            DeliveryErrorClassifier.Classify(new HttpRequestException("failed", inner: null, statusCode)));

    [Fact]
    public void Classify_http_without_status_code_returns_other() =>
        Assert.Equal(
            DeliveryErrorTypes.Other,
            DeliveryErrorClassifier.Classify(new HttpRequestException("connection refused on port 443")));

    [Fact]
    public void Classify_timeout_exception_returns_timeout() =>
        Assert.Equal(
            DeliveryErrorTypes.Timeout,
            DeliveryErrorClassifier.Classify(new TimeoutException("The operation timed out.")));

    [Fact]
    public void Classify_task_canceled_with_timeout_message_returns_timeout() =>
        Assert.Equal(
            DeliveryErrorTypes.Timeout,
            DeliveryErrorClassifier.Classify(
                new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")));

    [Fact]
    public void Classify_task_canceled_without_timeout_hint_returns_other() =>
        Assert.Equal(
            DeliveryErrorTypes.Other,
            DeliveryErrorClassifier.Classify(new TaskCanceledException("A task was canceled.")));

    [Fact]
    public void Classify_inner_http_exception_returns_http_status() =>
        Assert.Equal(
            DeliveryErrorTypes.Http4xx,
            DeliveryErrorClassifier.Classify(
                new InvalidOperationException(
                    "dispatch failed",
                    new HttpRequestException("failed", inner: null, HttpStatusCode.Unauthorized))));
}
