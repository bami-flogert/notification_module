namespace NotificationModule.Shared.Models;

public sealed record ScheduledReminderPlan(
    string ReminderType,
    DateTimeOffset ScheduledSendAt,
    bool CatchUp);
