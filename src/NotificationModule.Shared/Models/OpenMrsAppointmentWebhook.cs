using System.Text.Json.Serialization;

namespace NotificationModule.Shared.Models;

/// <summary>
/// Payload emitted by the OpenMRS Notification Bridge OMOD.
/// </summary>
public sealed record OpenMrsAppointmentWebhook
{
    [JsonPropertyName("event")]
    public string Event { get; init; } = string.Empty;

    [JsonPropertyName("appointmentUuid")]
    public string AppointmentUuid { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("startDateTime")]
    public DateTime StartDateTime { get; init; }

    [JsonPropertyName("endDateTime")]
    public DateTime? EndDateTime { get; init; }

    [JsonPropertyName("patientUuid")]
    public string PatientUuid { get; init; } = string.Empty;

    [JsonPropertyName("patientName")]
    public string PatientName { get; init; } = string.Empty;

    [JsonPropertyName("patientPhone")]
    public string? PatientPhone { get; init; }

    [JsonPropertyName("patientEmail")]
    public string? PatientEmail { get; init; }

    [JsonPropertyName("service")]
    public string? Service { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("comments")]
    public string? Comments { get; init; }
}
