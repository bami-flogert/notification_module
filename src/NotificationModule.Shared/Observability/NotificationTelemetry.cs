using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

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

    /// <summary>
    /// Allowed <c>failure_reason</c> tag values: <c>deserialize</c>, <c>dispatch</c>, <c>exception</c>.
    /// </summary>
    public static readonly Counter<long> NotificationMessagesFailed = Meter.CreateCounter<long>(
        "notification_messages_failed_total",
        unit: "messages");

    /// <summary>
    /// Allowed <c>reason</c> tag values: <c>deserialize</c>, <c>max_retries</c>, <c>exception</c>.
    /// </summary>
    public static readonly Counter<long> NotificationMessagesDlq = Meter.CreateCounter<long>(
        "notification_messages_dlq_total",
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

    public static readonly Counter<long> DeliverySuccesses = Meter.CreateCounter<long>(
        "notification_delivery_success_total",
        unit: "deliveries");

    /// <summary>
    /// Allowed <c>error_type</c> tag values: <see cref="DeliveryErrorTypes"/>.
    /// </summary>
    public static readonly Counter<long> DeliveryFailures = Meter.CreateCounter<long>(
        "notification_delivery_failure_total",
        unit: "deliveries");

    public static readonly Histogram<double> EndToEndLatencySeconds = Meter.CreateHistogram<double>(
        "notification_end_to_end_latency_seconds",
        unit: "s");

    public static readonly Histogram<double> SchedulerCycleDurationMs = Meter.CreateHistogram<double>(
        "scheduler_cycle_duration_ms",
        unit: "ms");

    public static readonly Counter<long> SchedulerDueNotificationsCount = Meter.CreateCounter<long>(
        "scheduler_due_notifications_count",
        unit: "notifications");

    // Observable gauges updated by the scheduler to reflect current pending queue state
    // Use Interlocked on backing long fields to avoid volatile on long/double which is not allowed.
    private static long PendingNotificationsBacking = 0;
    private static long PendingOldestBits = 0; // stores Double as Int64 bits

    public static void SetPendingMetrics(long count, double oldestSeconds)
    {
        Interlocked.Exchange(ref PendingNotificationsBacking, count);
        Interlocked.Exchange(ref PendingOldestBits, BitConverter.DoubleToInt64Bits(oldestSeconds));
    }

    public static readonly ObservableGauge<long> PendingNotificationsGauge = Meter.CreateObservableGauge<long>(
        "notification_pending_count",
        () => new[] { new Measurement<long>(Interlocked.Read(ref PendingNotificationsBacking)) },
        unit: "notifications");

    public static readonly ObservableGauge<double> PendingOldestGauge = Meter.CreateObservableGauge<double>(
        "notification_pending_oldest_seconds",
        () => new[] { new Measurement<double>(BitConverter.Int64BitsToDouble(Interlocked.Read(ref PendingOldestBits))) },
        unit: "seconds");

    // Provider retry metrics
    public static readonly Counter<long> ProviderRetryAttempts = Meter.CreateCounter<long>(
        "notification_provider_retry_attempts_total",
        unit: "attempts");

    public static readonly Histogram<double> ProviderRetryAttemptCount = Meter.CreateHistogram<double>(
        "notification_provider_retry_attempt_count",
        unit: "attempts");
}
