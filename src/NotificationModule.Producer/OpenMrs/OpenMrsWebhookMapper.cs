using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.OpenMrs;

public sealed class OpenMrsWebhookMapper
{
    public AppointmentMessage ToAppointmentMessage(OpenMrsAppointmentWebhook webhook, string organizationKey)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        if (string.IsNullOrWhiteSpace(webhook.AppointmentUuid))
            throw new ArgumentException("appointmentUuid is required.", nameof(webhook));

        if (webhook.StartDateTime == default && !IsCancelledEvent(webhook))
            throw new ArgumentException("startDateTime is required.", nameof(webhook));

        var status = ResolveStatus(webhook);

        return new AppointmentMessage
        {
            AppointmentUuid = webhook.AppointmentUuid.Trim(),
            PatientUuid = webhook.PatientUuid?.Trim() ?? string.Empty,
            PatientName = webhook.PatientName?.Trim() ?? string.Empty,
            PatientPhone = webhook.PatientPhone?.Trim() ?? string.Empty,
            PatientEmail = webhook.PatientEmail?.Trim() ?? string.Empty,
            StartDateTime = webhook.StartDateTime,
            Status = status,
            OrganizationKey = organizationKey,
            Location = webhook.Location?.Trim() ?? string.Empty,
            Instructions = BuildInstructions(webhook),
        };
    }

    private static string ResolveStatus(OpenMrsAppointmentWebhook webhook)
    {
        if (IsCancelledEvent(webhook))
            return "Cancelled";

        return string.IsNullOrWhiteSpace(webhook.Status)
            ? "Scheduled"
            : webhook.Status.Trim();
    }

    private static bool IsCancelledEvent(OpenMrsAppointmentWebhook webhook) =>
        string.Equals(webhook.Event, "CANCELLED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(webhook.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(webhook.Status, "Canceled", StringComparison.OrdinalIgnoreCase);

    private static string BuildInstructions(OpenMrsAppointmentWebhook webhook)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(webhook.Service))
            parts.Add($"Service: {webhook.Service.Trim()}");

        if (!string.IsNullOrWhiteSpace(webhook.Comments))
            parts.Add(webhook.Comments.Trim());

        return string.Join(Environment.NewLine, parts);
    }
}
