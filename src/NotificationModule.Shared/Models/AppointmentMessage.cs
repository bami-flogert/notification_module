using System;

namespace NotificationModule.Shared.Models;

/// <summary>
/// The canonical message that flows through RabbitMQ.
/// Produced by the API, consumed by the Worker.
/// </summary>
public record AppointmentMessage
{
    public string AppointmentUuid { get; init; } = string.Empty;
    public string PatientUuid     { get; init; } = string.Empty;
    public string PatientName     { get; init; } = string.Empty;
    public string PatientPhone    { get; init; } = string.Empty;
    public string PatientEmail    { get; init; } = string.Empty;
    public DateTime StartDateTime { get; init; }
    public string Status          { get; init; } = string.Empty;
    public string OrganizationKey { get; init; } = string.Empty;
    public string Location        { get; init; } = string.Empty;
    public string Instructions    { get; init; } = string.Empty;
    public Guid? ScheduledNotificationId { get; init; }
    public string ReminderType    { get; init; } = string.Empty;

    public string TargetProvider  { get; init; } = string.Empty;
    public string TriedProviders  { get; init; } = string.Empty;
}

