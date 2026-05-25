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
            sb.Append($"\n{message.Instructions}");

        return sb.ToString();
    }

    private static DateTimeOffset ToLocalTime(DateTime utcDateTime, string? ianaTimeZone)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZone))
            return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZone);
            var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
            var offset = tz.GetUtcOffset(utc);
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            return new DateTimeOffset(localDateTime, offset);
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);
        }
    }
}
