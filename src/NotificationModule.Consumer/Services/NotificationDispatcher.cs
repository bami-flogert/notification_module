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

    /// <summary>
    /// Dispatch to a single provider identified by channel name (case-insensitive).
    /// If channelName is null, dispatch to all providers (same behaviour as the other overload).
    /// </summary>
    public async Task DispatchAsync(AppointmentMessage message, string? channelName, CancellationToken ct)
    {
        IEnumerable<INotificationProvider> targets = channelName is null
            ? _providers
            : _providers.Where(p => string.Equals(p.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));

        var tasks = targets.Select(async provider =>
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

    public async Task<NotificationDispatchResult> DispatchToProviderAsync(
        AppointmentMessage message,
        string providerName,
        CancellationToken ct)
    {
        var provider = _providers.SingleOrDefault(p =>
            string.Equals(p.ChannelName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            return NotificationDispatchResult.Failed($"Provider '{providerName}' is not registered.");

        try
        {
            _logger.LogInformation("Sending via {Channel} for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            await provider.SendAsync(message, ct);

            _logger.LogInformation("{Channel} succeeded for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            return NotificationDispatchResult.Succeeded(provider.ChannelName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Channel} failed for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            return NotificationDispatchResult.Failed(ex.Message, provider.ChannelName);
        }
    }
}

public sealed record NotificationDispatchResult(string Provider, bool Success, string? ErrorMessage)
{
    public static NotificationDispatchResult Succeeded(string provider) =>
        new(provider, true, null);

    public static NotificationDispatchResult Failed(string errorMessage, string provider = "") =>
        new(provider, false, errorMessage);
}

