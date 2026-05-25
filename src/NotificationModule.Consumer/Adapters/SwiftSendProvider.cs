using System.Net.Http.Json;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Observability;

namespace NotificationModule.Consumer.Adapters;

/// <summary>SwiftSend: REST API authenticated with X-API-KEY header.</summary>
public class SwiftSendProvider : INotificationProvider
{
    public string ChannelName => "SwiftSend";

    private readonly HttpClient _http;
    private readonly ProviderSecretsStore _secrets;
    private readonly string _studentGroup;
    private readonly ILogger<SwiftSendProvider> _logger;

    public SwiftSendProvider(
        ProviderSecretsStore secrets,
        IConfiguration config,
        ILogger<SwiftSendProvider> logger)
    {
        _secrets = secrets;
        _logger = logger;
        var baseUrl = config["Providers:SwiftSend:BaseUrl"]
            ?? throw new InvalidOperationException("Providers:SwiftSend:BaseUrl is required.");

        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
    }

    public async Task<string?> SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var orgSecrets = await _secrets.GetForOrganizationAsync(message.OrganizationKey, ct);

        var content = NotificationMessageBuilder.Build(message);

        var body = new
        {
            type = "SMS",
            recipients = new[] { message.PatientPhone },
            content,
        };

        using var response = await PostJsonWithRetryAsync("/swiftsend", body, orgSecrets.SwiftSend.ApiKey, ct);
        ProviderLogging.LogHttpResult(_logger, ChannelName, message, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ProviderResponseIds.TryParseSwiftSendMessageId(json);
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
                using var request = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = JsonContent.Create(body),
                };
                request.Headers.Add("X-API-KEY", apiKey);

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
                // count this as a retry attempt
                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "SwiftSend transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        using var lastRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        lastRequest.Headers.Add("X-API-KEY", apiKey);
        var finalResp = await _http.SendAsync(lastRequest, ct);
        NotificationTelemetry.ProviderRetryAttemptCount.Record(
            maxAttempts,
            new KeyValuePair<string, object?>("provider", ChannelName));
        return finalResp;
    }
}
