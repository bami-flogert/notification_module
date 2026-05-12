using System.Net.Http.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>SwiftSend: REST API authenticated with X-API-KEY header.</summary>
public class SwiftSendProvider : INotificationProvider
{
    public string ChannelName => "SwiftSend";

    private readonly HttpClient _http;
    private readonly ILogger<SwiftSendProvider> _logger;

    public SwiftSendProvider(IConfiguration config, ILogger<SwiftSendProvider> logger)
    {
        _logger = logger;
        _http   = new HttpClient { BaseAddress = new Uri(config["Providers:SwiftSend:BaseUrl"]!) };
        _http.DefaultRequestHeaders.Add("X-API-KEY", config["Providers:SwiftSend:ApiKey"]);
        _http.DefaultRequestHeaders.Add(
            "X-STUDENT-GROUP",
            config["Providers:StudentGroup"] ?? "unknown-group");
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        // FakeComWorld SwiftSend expects POST /swiftsend with this schema:
        // { type: "SMS"|"EMAIL", recipients: string[], content: string }
        var body = new
        {
            type       = "SMS",
            recipients = new[] { message.PatientPhone },
            content    = FormatSmsText(message),
        };

        using var response = await PostJsonWithRetryAsync("/swiftsend", body, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string FormatSmsText(AppointmentMessage m) =>
        $"Hi {m.PatientName}, your appointment is confirmed for " +
        $"{m.StartDateTime:dd MMM yyyy HH:mm} UTC. Status: {m.Status}.";

    private async Task<HttpResponseMessage> PostJsonWithRetryAsync(string path, object body, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(path, body, ct);
                if (response.IsSuccessStatusCode)
                    return response;

                var code = (int)response.StatusCode;
                if (attempt == maxAttempts || (code < 500 && code != 408 && code != 429))
                    return response;

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "SwiftSend transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        // unreachable, but keeps compiler happy
        return await _http.PostAsJsonAsync(path, body, ct);
    }
}


