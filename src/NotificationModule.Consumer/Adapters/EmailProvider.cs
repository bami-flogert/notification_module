using System.Net.Http.Headers;
using System.Text;
using NotificationModule.Shared.Models;

namespace NotificationModule.Consumer.Adapters;

/// <summary>LegacyLink: SOAP API with HTTP Basic authentication.</summary>
public class EmailProvider : INotificationProvider
{
    public string ChannelName => "Email";

    private readonly HttpClient _http;

    public EmailProvider(IConfiguration config, ILogger<EmailProvider> logger)
    {
        var baseUrl  = config["Providers:LegacyLink:BaseUrl"]!;
        var username = config["Providers:LegacyLink:Username"]!;
        var password = config["Providers:LegacyLink:Password"]!;

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Add("SOAPAction", "SendEmailNotification");
    }

    public async Task SendAsync(AppointmentMessage message, CancellationToken ct)
    {
        var soapEnvelope = BuildSoapEnvelope(message);
        var content      = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

        var response = await _http.PostAsync("/soap/email", content, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildSoapEnvelope(AppointmentMessage m) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <SendEmailNotification xmlns="http://legacylink.example/email">
              <To>{m.PatientEmail}</To>
              <Subject>Appointment Confirmation — {m.StartDateTime:dd MMM yyyy}</Subject>
              <Body>
                Dear {m.PatientName},
                Your appointment is scheduled for {m.StartDateTime:dd MMM yyyy HH:mm} UTC.
                Status: {m.Status}
              </Body>
            </SendEmailNotification>
          </soap:Body>
        </soap:Envelope>
        """;
}


