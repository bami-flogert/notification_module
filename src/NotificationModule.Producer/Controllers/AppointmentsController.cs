using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("api/[controller]")]
[ServiceFilter(typeof(AppointmentApiKeyAuthFilter))]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentIngestionService _ingestionService;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(
        AppointmentIngestionService ingestionService,
        ILogger<AppointmentsController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    [HttpPost]
    [HttpPost("{organizationKey}")]
    public async Task<IActionResult> Post(
        [FromBody] AppointmentMessage message,
        string? organizationKey,
        [FromHeader(Name = "X-Organization-Key")] string? organizationHeader,
        CancellationToken cancellationToken)
    {
        if (message is null)
            return BadRequest("Body is required.");

        _logger.LogInformation("Received appointment {Uuid} for patient {Patient}",
            message.AppointmentUuid, message.PatientName);

        AppointmentIngestionResult result;
        try
        {
            result = await _ingestionService.IngestAsync(
                message,
                organizationKey ?? organizationHeader,
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

        _logger.LogInformation(
            "Saved appointment {Uuid} for organization {OrganizationKey}",
            result.AppointmentUuid,
            result.OrganizationKey);

        return Accepted(new
        {
            message = "Appointment saved.",
            appointmentUuid = result.AppointmentUuid,
            organizationKey = result.OrganizationKey,
            pendingNotifications = result.PendingNotificationCount,
        });
    }
}

