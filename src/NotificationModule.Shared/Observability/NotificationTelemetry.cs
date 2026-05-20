using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NotificationModule.Shared.Observability;

public static class NotificationTelemetry
{
    public const string ActivitySourceName = "NotificationModule";
    public const string MeterName = "NotificationModule";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AppointmentsIngested = Meter.CreateCounter<long>(
        "appointments_ingested_total",
        unit: "appointments");

    public static readonly Counter<long> ScheduledNotificationsCreated = Meter.CreateCounter<long>(
        "scheduled_notifications_created_total",
        unit: "notifications");

    public static readonly Counter<long> ScheduledNotificationsPublished = Meter.CreateCounter<long>(
        "scheduled_notifications_published_total",
        unit: "notifications");

    public static readonly Counter<long> NotificationDispatches = Meter.CreateCounter<long>(
        "notification_dispatch_total",
        unit: "dispatches");

    public static readonly Counter<long> NotificationMessagesReceived = Meter.CreateCounter<long>(
        "notification_messages_received_total",
        unit: "messages");

    public static readonly Counter<long> NotificationMessagesFailed = Meter.CreateCounter<long>(
        "notification_messages_failed_total",
        unit: "messages");

    public static readonly Counter<long> RabbitMqMessagesPublished = Meter.CreateCounter<long>(
        "rabbitmq_messages_published_total",
        unit: "messages");

    public static readonly Histogram<double> NotificationDispatchDurationMs = Meter.CreateHistogram<double>(
        "notification_dispatch_duration_ms",
        unit: "ms");

    public static readonly Counter<long> DeliveryTrackingWrites = Meter.CreateCounter<long>(
        "delivery_tracking_writes_total",
        unit: "writes");

    public static readonly Histogram<double> SchedulerCycleDurationMs = Meter.CreateHistogram<double>(
        "scheduler_cycle_duration_ms",
        unit: "ms");

    public static readonly Counter<long> SchedulerDueNotificationsCount = Meter.CreateCounter<long>(
        "scheduler_due_notifications_count",
        unit: "notifications");
}
