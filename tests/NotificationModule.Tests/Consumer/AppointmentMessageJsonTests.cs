using System.Text;
using System.Text.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Tests.Consumer;

/// <summary>
/// Mirrors <see cref="NotificationModule.Consumer.Workers.NotificationWorker"/> JSON deserialize options.
/// </summary>
public sealed class AppointmentMessageJsonTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserialize_returns_null_for_empty_object_without_required_fields()
    {
        var message = JsonSerializer.Deserialize<AppointmentMessage>("{}", Options);
        Assert.NotNull(message);
    }

    [Fact]
    public void Deserialize_throws_JsonException_for_malformed_json()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AppointmentMessage>("{not-json", Options));
    }

    [Fact]
    public void Deserialize_succeeds_for_valid_appointment_message_json()
    {
        var json = """
            {
              "appointmentUuid": "appt-1",
              "organizationKey": "default",
              "startDateTime": "2026-06-01T12:00:00Z",
              "status": "confirmed"
            }
            """;

        var message = JsonSerializer.Deserialize<AppointmentMessage>(json, Options);

        Assert.NotNull(message);
        Assert.Equal("appt-1", message!.AppointmentUuid);
    }

    [Fact]
    public void Utf8_decode_matches_worker_encoding()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"appointmentUuid":"appt-1"}""");
        var json = Encoding.UTF8.GetString(bytes);
        var message = JsonSerializer.Deserialize<AppointmentMessage>(json, Options);
        Assert.Equal("appt-1", message!.AppointmentUuid);
    }
}
