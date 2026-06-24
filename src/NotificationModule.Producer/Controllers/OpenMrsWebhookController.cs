using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.OpenMrs;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("api/webhooks/openmrs/appointments")]
[ServiceFilter(typeof(AppointmentApiKeyAuthFilter))]
public sealed class OpenMrsWebhookController : ControllerBase
{
    private readonly OpenMrsWebhookMapper _mapper;
    private readonly AppointmentIngestionService _ingestionService;
    private readonly ILogger<OpenMrsWebhookController> _logger;

    public OpenMrsWebhookController(
        OpenMrsWebhookMapper mapper,
        AppointmentIngestionService ingestionService,
        ILogger<OpenMrsWebhookController> logger)
    {
        _mapper = mapper;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost]
    [HttpPost("{organizationKey}")]
    public async Task<IActionResult> Post(
        [FromBody] OpenMrsAppointmentWebhook webhook,
        string? organizationKey,
        [FromHeader(Name = "X-Organization-Key")] string? organizationHeader,
        CancellationToken cancellationToken)
    {
        if (webhook is null)
            return BadRequest("Body is required.");

        var resolvedOrg = organizationKey ?? organizationHeader ?? string.Empty;

        _logger.LogInformation(
            "OpenMRS webhook {Event} for appointment {AppointmentUuid}",
            webhook.Event,
            webhook.AppointmentUuid);

        AppointmentMessage message;
        try
        {
            message = _mapper.ToAppointmentMessage(webhook, resolvedOrg);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        AppointmentIngestionResult result;
        try
        {
            result = await _ingestionService.IngestAsync(
                message,
                string.IsNullOrWhiteSpace(resolvedOrg) ? null : resolvedOrg,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        return Accepted(new
        {
            message = "Appointment saved.",
            eventType = webhook.Event,
            appointmentUuid = result.AppointmentUuid,
            organizationKey = result.OrganizationKey,
            pendingNotifications = result.PendingNotificationCount,
            scheduledReminders = result.ScheduledReminders.Select(r => new
            {
                reminderType = r.ReminderType,
                scheduledSendAt = r.ScheduledSendAt,
                catchUp = r.CatchUp,
            }),
        });
    }
}
