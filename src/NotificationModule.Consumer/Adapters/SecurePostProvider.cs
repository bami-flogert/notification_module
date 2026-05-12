using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>SecurePost: obtains a JWT token first, then posts the message.</summary>
public class SecurePostProvider : INotificationProvider
{
    public string ChannelName => "SecurePost";

    private readonly HttpClient _http;
    private readonly string _studentGroup;
    private readonly string _authEndpoint;
    private readonly string _messageEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ILogger<SecurePostProvider> _logger;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public SecurePostProvider(IConfiguration config, ILogger<SecurePostProvider> logger)
    {
        _logger = logger;
        var baseUrl  = config["Providers:SecurePost:BaseUrl"]!;
        _studentGroup   = config["Providers:StudentGroup"] ?? "unknown-group";
        _authEndpoint   = config["Providers:SecurePost:AuthEndpoint"] ?? "/securepost/auth";
        _messageEndpoint = config["Providers:SecurePost:MessageEndpoint"] ?? "/securepost/message";

        _clientId     = config["Providers:SecurePost:ClientId"] ?? "securepost-client-id";
        _clientSecret = config["Providers:SecurePost:ClientSecret"] ?? "securepost-secret-key";

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            format    = "SMS",
            recipient = message.PatientPhone,
            body      = $"Appointment reminder for {message.PatientName}: " +
                        $"{message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
            subject   = $"Appointment reminder — {message.StartDateTime:dd MMM yyyy}",
        };

        using var response = await PostJsonWithRetryAsync(_messageEndpoint, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        // FakeComWorld SecurePost auth expects:
        // POST /securepost/auth { clientId, clientSecret } → { accessToken, expiresIn, ... }
        using var response = await PostJsonWithRetryAsync(_authEndpoint,
            new { clientId = _clientId, clientSecret = _clientSecret }, ct);

        response.EnsureSuccessStatusCode();

        var json  = await response.Content.ReadAsStringAsync(ct);
        var doc   = JsonDocument.Parse(json);
        _cachedToken = doc.RootElement.GetProperty("accessToken").GetString()!;

        var expiresInSeconds = doc.RootElement.TryGetProperty("expiresIn", out var expiresInEl)
            ? expiresInEl.GetInt32()
            : 180;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(0, expiresInSeconds - 10)); // refresh slightly early

        return _cachedToken;
    }

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
                _logger.LogWarning(ex, "SecurePost transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        return await _http.PostAsJsonAsync(path, body, ct);
    }
}


