using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>
/// Implement this interface to add a new notification channel.
/// The dispatcher will call SendAsync for every message on the queue.
/// </summary>
public interface INotificationProvider
{
    string ChannelName { get; }   // "SMS", "WhatsApp", "Email"

    /// <summary>
    /// Sends the notification and returns the provider's external message/tracking ID when present.
    /// </summary>
    Task<string?> SendAsync(AppointmentMessage message, CancellationToken ct);
}

