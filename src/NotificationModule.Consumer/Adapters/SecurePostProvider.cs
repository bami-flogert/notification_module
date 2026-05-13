using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NotificationModule.Consumer.Secrets;
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

    public SecurePostProvider(
        ProviderSecretsStore secrets,
        IConfiguration config,
        ILogger<SecurePostProvider> logger)
    {
        _logger = logger;
        var baseUrl = config["Providers:SecurePost:BaseUrl"]
            ?? throw new InvalidOperationException("Providers:SecurePost:BaseUrl is required.");

        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";
        _authEndpoint = config["Providers:SecurePost:AuthEndpoint"]
            ?? throw new InvalidOperationException("Providers:SecurePost:AuthEndpoint is required.");
        _messageEndpoint = config["Providers:SecurePost:MessageEndpoint"]
            ?? throw new InvalidOperationException("Providers:SecurePost:MessageEndpoint is required.");

        _clientId = secrets.SecurePost.ClientId;
        _clientSecret = secrets.SecurePost.ClientSecret;

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            format = "SMS",
            recipient = message.PatientPhone,
            body = $"Appointment reminder for {message.PatientName}: " +
                   $"{message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
            subject = $"Appointment reminder — {message.StartDateTime:dd MMM yyyy}",
        };

        using var response = await PostJsonWithRetryAsync(_messageEndpoint, body, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        using var response = await PostJsonWithRetryAsync(
            _authEndpoint,
            new { clientId = _clientId, clientSecret = _clientSecret },
            ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        _cachedToken = doc.RootElement.GetProperty("accessToken").GetString()!;

        var expiresInSeconds = doc.RootElement.TryGetProperty("expiresIn", out var expiresInEl)
            ? expiresInEl.GetInt32()
            : 180;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(0, expiresInSeconds - 10));

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
