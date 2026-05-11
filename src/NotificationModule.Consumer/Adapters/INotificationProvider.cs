using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>
/// Implement this interface to add a new notification channel.
/// The dispatcher will call SendAsync for every message on the queue.
/// </summary>
public interface INotificationProvider
{
    string ChannelName { get; }   // "SMS", "WhatsApp", "Email"
    Task SendAsync(AppointmentMessage message, CancellationToken ct);
}

