using System.Text;
using NotificationModule.Shared.Models;

namespace NotificationModule.Shared;

public static class NotificationMessageBuilder
{
    public static string Build(AppointmentMessage message)
    {
        var localTime = ToLocalTime(message.StartDateTime, message.TimeZone);
        var sb = new StringBuilder();

        if (string.IsNullOrWhiteSpace(message.PatientName))
            sb.Append($"Your appointment is {message.Status} for {localTime:dd MMM yyyy HH:mm zzz}.");
        else
            sb.Append($"Hi {message.PatientName}, your appointment is {message.Status} for {localTime:dd MMM yyyy HH:mm zzz}.");

        if (!string.IsNullOrWhiteSpace(message.Location))
            sb.Append($"\nLocation: {message.Location}");

        if (!string.IsNullOrWhiteSpace(message.Instructions))
            sb.Append($"\nInstructions: {message.Instructions}");

        return sb.ToString();
    }

    private static DateTimeOffset ToLocalTime(DateTime utcDateTime, string? ianaTimeZone)
    {
        if (!OrganizationTimeZone.TryGetTimeZoneInfo(ianaTimeZone, out var tz))
            return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);

        return OrganizationTimeZone.ConvertUtcToLocalDisplay(utcDateTime, tz);
    }
}
