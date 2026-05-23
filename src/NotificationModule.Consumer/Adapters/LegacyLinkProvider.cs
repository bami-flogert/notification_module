using System.Net.Http.Headers;
using System.Text;
using NotificationModule.Consumer.Secrets;
using NotificationModule.Shared.Models;
using NotificationModule.Shared.Observability;

namespace NotificationModule.Consumer.Adapters;

/// <summary>LegacyLink: XML API with HTTP Basic authentication.</summary>
public class LegacyLinkProvider : INotificationProvider
{
    public string ChannelName => "LegacyLink";

    private readonly HttpClient _http;
    private readonly ProviderSecretsStore _secrets;
    private readonly string _studentGroup;
    private readonly ILogger<LegacyLinkProvider> _logger;

    public LegacyLinkProvider(
        ProviderSecretsStore secrets,
        IConfiguration config,
        ILogger<LegacyLinkProvider> logger)
    {
        _secrets = secrets;
        _logger = logger;
        var baseUrl = config["Providers:LegacyLink:BaseUrl"]
            ?? throw new InvalidOperationException("Providers:LegacyLink:BaseUrl is required.");

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _studentGroup = config["Providers:StudentGroup"] ?? "unknown-group";
        _http.DefaultRequestHeaders.Add("X-STUDENT-GROUP", _studentGroup);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var orgSecrets = await _secrets.GetForOrganizationAsync(message.OrganizationKey, ct);
        var xmlBody = BuildLegacyLinkSendSmsXml(message);

        using var response = await PostXmlWithRetryAsync(
            "/LegacyLink/SendSms",
            xmlBody,
            orgSecrets.LegacyLink.Username,
            orgSecrets.LegacyLink.Password,
            ct);
        ProviderLogging.LogHttpResult(_logger, ChannelName, message, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildLegacyLinkSendSmsXml(AppointmentMessage m)
    {
        var text =
            $"Appointment reminder for {m.PatientName}: {m.StartDateTime:dd MMM yyyy HH:mm} UTC — {m.Status}";

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <SendSmsRequest xmlns="http://legacylink.fakecomworld.com/v1">
              <PhoneNumber>{EscapeXml(m.PatientPhone)}</PhoneNumber>
              <MessageText>{EscapeXml(text)}</MessageText>
              <SenderIdentification>NotificationModule</SenderIdentification>
            </SendSmsRequest>
            """;
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private async Task<HttpResponseMessage> PostXmlWithRetryAsync(
        string path,
        string xmlBody,
        string username,
        string password,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = CreateXmlRequest(path, xmlBody, username, password);
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
                _logger.LogWarning(ex, "LegacyLink transient error (attempt {Attempt}/{Max}). Retrying…", attempt, maxAttempts);
                NotificationTelemetry.ProviderRetryAttempts.Add(
                    1,
                    new KeyValuePair<string, object?>("provider", ChannelName),
                    new KeyValuePair<string, object?>("attempt", attempt));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        using var lastRequest = CreateXmlRequest(path, xmlBody, username, password);
        var finalResp = await _http.SendAsync(lastRequest, ct);
        NotificationTelemetry.ProviderRetryAttemptCount.Record(
            maxAttempts,
            new KeyValuePair<string, object?>("provider", ChannelName));
        return finalResp;
    }

    private static HttpRequestMessage CreateXmlRequest(
        string path,
        string xmlBody,
        string username,
        string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(xmlBody, Encoding.UTF8, "application/xml"),
        };

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }
}
