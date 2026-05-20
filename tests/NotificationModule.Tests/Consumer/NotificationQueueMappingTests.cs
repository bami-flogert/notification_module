using NotificationModule.Consumer.Workers;

namespace NotificationModule.Tests.Consumer;

public sealed class NotificationQueueMappingTests
{
    [Theory]
    [InlineData("notifications.swiftsend", "SwiftSend")]
    [InlineData("notifications.securepost", "SecurePost")]
    [InlineData("notifications.legacylink", "LegacyLink")]
    [InlineData("notifications.asyncflow", "AsyncFlow")]
    public void TryGetProviderName_maps_known_queues(string queue, string expected) =>
        Assert.Equal(expected, NotificationQueueMapping.TryGetProviderName(queue));

    [Fact]
    public void TryGetProviderName_returns_null_for_unknown_queue() =>
        Assert.Null(NotificationQueueMapping.TryGetProviderName("notifications.unknown"));
}
