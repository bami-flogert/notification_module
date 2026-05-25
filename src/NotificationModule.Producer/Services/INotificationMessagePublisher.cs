using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Services;

public interface INotificationMessagePublisher
{
    void Publish(AppointmentMessage message);
}
