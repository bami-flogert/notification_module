using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(RabbitMqPublisher publisher, ILogger<AppointmentsController> logger)
    {
        _publisher = publisher;
        _logger    = logger;
    }

    [HttpPost]
    public IActionResult Post([FromBody] AppointmentMessage message)
    {
        if (message is null)
            return BadRequest("Body is required.");

        _logger.LogInformation("Received appointment {Uuid} for patient {Patient}",
            message.AppointmentUuid, message.PatientName);

        _publisher.Publish(message);

        _logger.LogInformation("Published appointment {Uuid} to RabbitMQ", message.AppointmentUuid);

        return Accepted(new { message = "Notification queued.", appointmentUuid = message.AppointmentUuid });
    }
}

