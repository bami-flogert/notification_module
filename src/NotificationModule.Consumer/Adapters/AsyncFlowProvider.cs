using System.Net.Http.Json;
using System.Text.Json;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Observability;

namespace NotificationModule.Consumer.Adapters;

/// <summary>
/// AsyncFlow: asynchronous REST API. Submit via POST /asyncflow (202 Accepted)
/// and poll GET /asyncflow/{trackingId} until Completed/Failed.
/// </summary>
public class AsyncFlowProvider : INotificationProvider
{
    public string ChannelName => "AsyncFlow";

    private readonly HttpClient _http;
    private readonly ProviderSecretsStore _secrets;
    private readonly ILogger<AsyncFlowProvider> _logger;
    private readonly string _studentGroup;

    public AsyncFlowProvider(
        ProviderSecretsStore secrets,
        IConfiguration config,
        ILogger<AsyncFlowProvider> logger)
    {
        _secrets = secrets;
        _logger = logger;

        var baseUrl = config["Providers:AsyncFlow:BaseUrl"]
            ?? config["Providers:AsyncFlow:BaseURL"]
            ?? throw new InvalidOperationException("Providers:AsyncFlow:BaseUrl is required.");

        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var orgSecrets = await _secrets.GetForOrganizationAsync(message.OrganizationKey, ct);
        var apiKey = orgSecrets.AsyncFlow.ApiKey;

        var submitBody = new
        {
            destination = message.PatientPhone,
            content =
                $"Appointment reminder for {message.PatientName}: {message.StartDateTime:dd MMM yyyy HH:mm} UTC — {message.Status}",
            priority = "normal",
        };

        using var submitResponse = await PostJsonWithRetryAsync("/asyncflow", submitBody, apiKey, ct);
        ProviderLogging.LogHttpResult(_logger, ChannelName, message, (int)submitResponse.StatusCode);
        submitResponse.EnsureSuccessStatusCode();

        var submitJson = await submitResponse.Content.ReadAsStringAsync(ct);
        var trackingId = ExtractTrackingId(submitJson);
        if (string.IsNullOrWhiteSpace(trackingId))
            throw new InvalidOperationException("AsyncFlow submit succeeded but no trackingId was returned.");

        await WaitForCompletionAsync(trackingId, apiKey, ct);
    }

    private async Task WaitForCompletionAsync(string trackingId, string apiKey, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        var delayMs = 400;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            using var statusResponse = await GetWithRetryAsync($"/asyncflow/{trackingId}", apiKey, ct);
            statusResponse.EnsureSuccessStatusCode();

            var json = await statusResponse.Content.ReadAsStringAsync(ct);
            var status = ExtractStatus(json);

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"AsyncFlow reported Failed for trackingId {trackingId}.");

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

    private async Task<HttpResponseMessage> PostJsonWithRetryAsync(
        string path,
        object body,
        string apiKey,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = CreateJsonRequest(HttpMethod.Post, path, body, apiKey);
                var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    NotificationTelemetry.ProviderRetryAttemptCount.Record(
                        attempt,
                        new KeyValuePair<string, object?>("provider", ChannelName));
                    return response;
                }

                var code = (int)response.StatusCode;
                if (attempt == maxAttempts || (code < 500 && code != 408 && code != 429))
                    {
                        NotificationTelemetry.ProviderRetryAttemptCount.Record(
                            attempt,
                            new KeyValuePair<string, object?>("provider", ChannelName));
                        return response;
                    }

                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "AsyncFlow transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        using var lastRequest = CreateJsonRequest(HttpMethod.Post, path, body, apiKey);
        var finalResp = await _http.SendAsync(lastRequest, ct);
        NotificationTelemetry.ProviderRetryAttemptCount.Record(
            maxAttempts,
            new KeyValuePair<string, object?>("provider", ChannelName));
        return finalResp;
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string path, string apiKey, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = CreateJsonRequest(HttpMethod.Get, path, body: null, apiKey);
                var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    NotificationTelemetry.ProviderRetryAttemptCount.Record(
                        attempt,
                        new KeyValuePair<string, object?>("provider", ChannelName));
                    return response;
                }

                var code = (int)response.StatusCode;
                if (attempt == maxAttempts || (code < 500 && code != 408 && code != 429))
                    {
                        NotificationTelemetry.ProviderRetryAttemptCount.Record(
                            attempt,
                            new KeyValuePair<string, object?>("provider", ChannelName));
                        return response;
                    }

                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "AsyncFlow transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
        }

        using var lastRequest = CreateJsonRequest(HttpMethod.Get, path, body: null, apiKey);
        var finalRespGet = await _http.SendAsync(lastRequest, ct);
        NotificationTelemetry.ProviderRetryAttemptCount.Record(
            maxAttempts,
            new KeyValuePair<string, object?>("provider", ChannelName));
        return finalRespGet;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object? body, string apiKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-API-KEY", apiKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }
}
