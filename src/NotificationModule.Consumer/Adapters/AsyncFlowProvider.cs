using System.Net.Http.Json;
using System.Text.Json;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>
/// AsyncFlow: asynchronous REST API. Submit via POST /asyncflow (202 Accepted)
/// and poll GET /asyncflow/{trackingId} until Completed/Failed.
/// </summary>
public class AsyncFlowProvider : INotificationProvider
{
    public string ChannelName => "AsyncFlow";

    private readonly HttpClient _http;
    private readonly ILogger<AsyncFlowProvider> _logger;
    private readonly string _studentGroup;
    private readonly string _apiKey;

    public AsyncFlowProvider(IConfiguration config, ILogger<AsyncFlowProvider> logger)
    {
        _logger = logger;

        var baseUrl = config["Providers:AsyncFlow:BaseUrl"] ?? config["Providers:AsyncFlow:BaseURL"]!;
        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";
        _apiKey = config["Providers:AsyncFlow:ApiKey"] ?? "asyncflow-api-key";

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
        _http.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var submitBody = new
        {
            destination = message.PatientPhone,
            content = $"Appointment reminder for {message.PatientName}: {message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
            priority = "normal",
        };

        using var submitResponse = await PostJsonWithRetryAsync("/asyncflow", submitBody, ct);
        submitResponse.EnsureSuccessStatusCode(); // should be 202

        var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);
        var trackingId = ExtractTrackingId(submitJson);
        if (string.IsNullOrWhiteSpace(trackingId))
            throw new InvalidOperationException($"AsyncFlow submit succeeded but no trackingId returned. Body: {submitJson}");

        await WaitForCompletionAsync(trackingId, ct);
    }

    private async Task WaitForCompletionAsync(string trackingId, CancellationToken ct)
    {
        // Keep polling short so the worker doesn't block too long in demo runs.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        var delayMs = 400;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            using var statusResponse = await GetWithRetryAsync($"/asyncflow/{trackingId}", ct);
            statusResponse.EnsureSuccessStatusCode();

            var json = await statusResponse.Content.ReadAsStringAsync(ct);
            var status = ExtractStatus(json);

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"AsyncFlow reported Failed for {trackingId}. Body: {json}");

            await Task.Delay(delayMs, ct);
            delayMs = Math.Min(delayMs * 2, 2000);
        }

        _logger.LogWarning("AsyncFlow did not complete within polling window for {TrackingId}", trackingId);
    }

    private static string? ExtractTrackingId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("trackingId", out var el) ? el.GetString() : null;
    }

    private static string? ExtractStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("status", out var el) ? el.GetString() : null;
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
                _logger.LogWarning(ex, "AsyncFlow transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        return await _http.PostAsJsonAsync(path, body, ct);
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string path, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _http.GetAsync(path, ct);
                if (response.IsSuccessStatusCode)
                    return response;

                var code = (int)response.StatusCode;
                if (attempt == maxAttempts || (code < 500 && code != 408 && code != 429))
                    return response;

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "AsyncFlow transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
        }

        return await _http.GetAsync(path, ct);
    }
}

