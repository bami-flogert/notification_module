using System.Net.Http.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>SwiftSend: REST API authenticated with X-API-KEY header.</summary>
public class SmsProvider : INotificationProvider
{
    public string ChannelName => "SMS";

    private readonly HttpClient _http;
    private readonly ILogger<SmsProvider> _logger;

    public SmsProvider(IConfiguration config, ILogger<SmsProvider> logger)
    {
        _logger = logger;
        _http   = new HttpClient { BaseAddress = new Uri(config["Providers:SwiftSend:BaseUrl"]!) };
        _http.DefaultRequestHeaders.Add("X-API-KEY", config["Providers:SwiftSend:ApiKey"]);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var body = new
        {
            to      = message.PatientPhone,
            message = FormatSmsText(message),
        };

        var response = await _http.PostAsJsonAsync("/api/sms", body, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string FormatSmsText(AppointmentMessage m) =>
        $"Hi {m.PatientName}, your appointment is confirmed for " +
        $"{m.StartDateTime:dd MMM yyyy HH:mm} UTC. Status: {m.Status}.";
}


