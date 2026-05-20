using System.Collections.Concurrent;
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
    private readonly ProviderSecretsStore _secrets;
    private readonly string _studentGroup;
    private readonly string _authEndpoint;
    private readonly string _messageEndpoint;
    private readonly ILogger<SecurePostProvider> _logger;
    private readonly ConcurrentDictionary<string, TokenCacheEntry> _tokenCache = new(StringComparer.OrdinalIgnoreCase);

    public SecurePostProvider(
        ProviderSecretsStore secrets,
        IConfiguration config,
        ILogger<SecurePostProvider> logger)
    {
        _secrets = secrets;
        _logger = logger;
        var baseUrl = config["Providers:SecurePost:BaseUrl"]
            ?? throw new InvalidOperationException("Providers:SecurePost:BaseUrl is required.");

        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";
        _authEndpoint = config["Providers:SecurePost:AuthEndpoint"]
            ?? throw new InvalidOperationException("Providers:SecurePost:AuthEndpoint is required.");
        _messageEndpoint = config["Providers:SecurePost:MessageEndpoint"]
            ?? throw new InvalidOperationException("Providers:SecurePost:MessageEndpoint is required.");

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var orgSecrets = await _secrets.GetForOrganizationAsync(message.OrganizationKey, ct);
        var token = await GetTokenAsync(message.OrganizationKey, orgSecrets.SecurePost, ct);

        var body = new
        {
            format = "SMS",
            recipient = message.PatientPhone,
            body = $"Appointment reminder for {message.PatientName}: " +
                   $"{message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
            subject = $"Appointment reminder — {message.StartDateTime:dd MMM yyyy}",
        };

        using var response = await PostWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, _messageEndpoint)
                {
                    Content = JsonContent.Create(body),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            },
            ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> GetTokenAsync(
        string organizationKey,
        SecurePostSecretPayload credentials,
        CancellationToken ct)
    {
        if (_tokenCache.TryGetValue(organizationKey, out var cached) && DateTime.UtcNow < cached.ExpiryUtc)
            return cached.Token;

        using var response = await PostWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, _authEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    clientId = credentials.ClientId,
                    clientSecret = credentials.ClientSecret,
                }),
            },
            ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("accessToken").GetString()!;

        var expiresInSeconds = doc.RootElement.TryGetProperty("expiresIn", out var expiresInEl)
            ? expiresInEl.GetInt32()
            : 180;
        var expiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, expiresInSeconds - 10));

        _tokenCache[organizationKey] = new TokenCacheEntry(token, expiryUtc);
        return token;
    }

    private async Task<HttpResponseMessage> PostWithRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = createRequest();
                var response = await _http.SendAsync(request, ct);
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

        using var lastRequest = createRequest();
        return await _http.SendAsync(lastRequest, ct);
    }

    private sealed record TokenCacheEntry(string Token, DateTime ExpiryUtc);
}
