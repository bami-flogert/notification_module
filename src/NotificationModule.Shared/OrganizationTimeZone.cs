namespace NotificationModule.Shared;

public static class OrganizationTimeZone
{
    public static bool TryGetTimeZoneInfo(string? ianaId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(ianaId))
            return false;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static DateTimeOffset ConvertUnspecifiedLocalToUtc(DateTime localValue, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(localValue, timeZone));

    public static DateTimeOffset ConvertUtcToLocalDisplay(DateTime utcDateTime, TimeZoneInfo timeZone)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var offset = timeZone.GetUtcOffset(utc);
        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        return new DateTimeOffset(localDateTime, offset);
    }
}
