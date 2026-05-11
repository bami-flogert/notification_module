using NotificationModule.Consumer.Adapters;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Services;

/// <summary>
/// Receives a deserialized message and fans it out to all registered providers.
/// To add a new provider: register it in DI — no changes here.
/// </summary>
public class NotificationDispatcher
{
    private readonly IEnumerable<INotificationProvider> _providers;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationProvider> providers,
        ILogger<NotificationDispatcher> logger)
    {
        _providers = providers;
        _logger    = logger;
    }

    public async Task DispatchAsync(AppointmentMessage message, CancellationToken ct)
    {
        var tasks = _providers.Select(async provider =>
        {
            try
            {
                _logger.LogInformation("Sending via {Channel} for {Uuid}",
                    provider.ChannelName, message.AppointmentUuid);

                await provider.SendAsync(message, ct);

                _logger.LogInformation("{Channel} succeeded for {Uuid}",
                    provider.ChannelName, message.AppointmentUuid);
            }
            catch (Exception ex)
            {
                // Log but don't fail other providers
                _logger.LogError(ex, "{Channel} failed for {Uuid}",
                    provider.ChannelName, message.AppointmentUuid);
            }
        });

        await Task.WhenAll(tasks);
    }
}

