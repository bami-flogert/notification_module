using NotificationModule.Consumer.Adapters;
using NotificationModule.Shared.Observability;
using NotificationModule.Shared.Models;
using System.Diagnostics;

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

    public Task DispatchAsync(AppointmentMessage message, CancellationToken ct)
        => DispatchAsync(message, null, ct);

    /// <summary>
    /// Dispatch to a single provider identified by channel name (case-insensitive).
    /// If channelName is null, dispatch to all providers (same behaviour as the other overload).
    /// </summary>
    public async Task DispatchAsync(AppointmentMessage message, string? channelName, CancellationToken ct)
    {
        IEnumerable<INotificationProvider> targets = channelName is null
            ? _providers
            : _providers.Where(p => string.Equals(p.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));

        var tasks = targets.Select(provider => DispatchToProviderAsync(message, provider.ChannelName, ct));
        var results = await Task.WhenAll(tasks);

        foreach (var result in results.Where(r => !r.Success))
        {
            _logger.LogError("Provider {Provider} failed for {Uuid}: {Error}",
                result.Provider,
                message.AppointmentUuid,
                result.ErrorMessage);
        }
    }

    public async Task<NotificationDispatchResult> DispatchToProviderAsync(
        AppointmentMessage message,
        string providerName,
        CancellationToken ct)
    {
        var provider = _providers.SingleOrDefault(p =>
            string.Equals(p.ChannelName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            return NotificationDispatchResult.Failed($"Provider '{providerName}' is not registered.", providerName);

        var started = Stopwatch.GetTimestamp();
        using var activity = NotificationTelemetry.ActivitySource.StartActivity(
            "consumer.dispatch.provider",
            ActivityKind.Client);

        try
        {
            _logger.LogInformation("Sending via {Channel} for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            await provider.SendAsync(message, ct);

            _logger.LogInformation("{Channel} succeeded for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            activity?.SetTag("provider", provider.ChannelName);
            activity?.SetTag("appointment.uuid", message.AppointmentUuid);
            activity?.SetTag("organization.key", message.OrganizationKey);
            activity?.SetTag("dispatch.status", "success");

            NotificationTelemetry.NotificationDispatches.Add(
                1,
                new KeyValuePair<string, object?>("provider", provider.ChannelName),
                new KeyValuePair<string, object?>("status", "success"));
            NotificationTelemetry.NotificationDispatchDurationMs.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", provider.ChannelName),
                new KeyValuePair<string, object?>("status", "success"));

            return NotificationDispatchResult.Succeeded(provider.ChannelName);
        }
        catch (Exception ex)
        {
            NotificationTelemetry.NotificationDispatches.Add(
                1,
                new KeyValuePair<string, object?>("provider", provider.ChannelName),
                new KeyValuePair<string, object?>("status", "failed"));
            NotificationTelemetry.NotificationDispatchDurationMs.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", provider.ChannelName),
                new KeyValuePair<string, object?>("status", "failed"));

            _logger.LogError(ex, "{Channel} failed for {Uuid}",
                provider.ChannelName, message.AppointmentUuid);

            activity?.SetTag("provider", provider.ChannelName);
            activity?.SetTag("appointment.uuid", message.AppointmentUuid);
            activity?.SetTag("organization.key", message.OrganizationKey);
            activity?.SetTag("dispatch.status", "failed");

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

