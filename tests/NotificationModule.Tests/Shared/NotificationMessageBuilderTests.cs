using NotificationModule.Shared;
using NotificationModule.Shared.Models;

namespace NotificationModule.Tests.Shared;

public sealed class NotificationMessageBuilderTests
{
    private static AppointmentMessage BaseMessage() => new()
    {
        PatientName   = "Jane Doe",
        StartDateTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        Status        = "confirmed",
        TimeZone      = "Europe/Amsterdam",
        Location      = "Room 3B, City Hospital",
        Instructions  = "Please bring your insurance card.",
    };

    [Fact]
    public void Build_includes_patient_name()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("Jane Doe", result);
    }

    [Fact]
    public void Build_starts_with_your_appointment_when_name_is_empty()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage() with { PatientName = "" });
        Assert.StartsWith("Your appointment", result);
    }

    [Fact]
    public void Build_includes_status()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("confirmed", result);
    }

    [Fact]
    public void Build_converts_utc_to_local_time()
    {
        // Europe/Amsterdam is UTC+2 in summer (CEST): 12:00 UTC → 14:00 local
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("14:00", result);
        Assert.DoesNotContain("12:00 UTC", result);
    }

    [Fact]
    public void Build_includes_utc_offset_in_formatted_time()
    {
        // CEST = +02:00
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("+02:00", result);
    }

    [Fact]
    public void Build_includes_location_on_new_line()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("\nLocation: Room 3B, City Hospital", result);
    }

    [Fact]
    public void Build_omits_location_when_empty()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage() with { Location = "" });
        Assert.DoesNotContain("Location:", result);
    }

    [Fact]
    public void Build_includes_instructions_on_new_line()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage());
        Assert.Contains("\nPlease bring your insurance card.", result);
    }

    [Fact]
    public void Build_omits_instructions_when_empty()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage() with { Instructions = "" });
        Assert.DoesNotContain("insurance card", result);
    }

    [Fact]
    public void Build_falls_back_to_utc_for_unknown_timezone()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage() with { TimeZone = "Unknown/Zone" });
        Assert.Contains("12:00", result);
        Assert.Contains("+00:00", result);
    }

    [Fact]
    public void Build_falls_back_to_utc_when_timezone_is_empty()
    {
        var result = NotificationMessageBuilder.Build(BaseMessage() with { TimeZone = "" });
        Assert.Contains("12:00", result);
        Assert.Contains("+00:00", result);
    }

    [Fact]
    public void Build_omits_both_optional_fields_when_both_absent()
    {
        var result = NotificationMessageBuilder.Build(
            BaseMessage() with { Location = "", Instructions = "" });
        Assert.DoesNotContain('\n', result);
    }
}
