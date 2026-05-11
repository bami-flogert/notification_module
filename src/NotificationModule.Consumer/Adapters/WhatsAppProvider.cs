using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>SecurePost: obtains a JWT token first, then posts the message.</summary>
public class WhatsAppProvider : INotificationProvider
{
    public string ChannelName => "WhatsApp";

    private readonly HttpClient _http;
    private readonly string _tokenUrl;
    private readonly string _username;
    private readonly string _password;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public WhatsAppProvider(IConfiguration config, ILogger<WhatsAppProvider> logger)
    {
        var baseUrl  = config["Providers:SecurePost:BaseUrl"]!;
        _tokenUrl    = config["Providers:SecurePost:TokenUrl"]!;
        _username    = config["Providers:SecurePost:Username"]!;
        _password    = config["Providers:SecurePost:Password"]!;
        _http        = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            to      = message.PatientPhone,
            message = $"Appointment reminder for {message.PatientName}: " +
                      $"{message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
        };

        var response = await _http.PostAsJsonAsync("/api/whatsapp", body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        var response = await _http.PostAsJsonAsync(_tokenUrl,
            new { username = _username, password = _password }, ct);

        response.EnsureSuccessStatusCode();

        var json  = await response.Content.ReadAsStringAsync(ct);
        var doc   = JsonDocument.Parse(json);
        _cachedToken = doc.RootElement.GetProperty("token").GetString()!;
        _tokenExpiry = DateTime.UtcNow.AddMinutes(55); // refresh before expiry

        return _cachedToken;
    }
}


