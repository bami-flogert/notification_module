using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("api/reports")]
[ServiceFilter(typeof(AppointmentApiKeyAuthFilter))]
public sealed class ReportsController : ControllerBase
{
    private readonly BillingDeliveriesReportService _reportService;

    public ReportsController(BillingDeliveriesReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries(
        [FromQuery] string organizationKey,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organizationKey))
            return BadRequest(new { message = "organizationKey is required." });

        if (from == default)
            return BadRequest(new { message = "from is required (ISO-8601)." });

        if (to == default)
            return BadRequest(new { message = "to is required (ISO-8601)." });

        if (from > to)
            return BadRequest(new { message = "from must be less than or equal to to." });

        var results = await _reportService.GetDeliveriesAsync(
            organizationKey.Trim(),
            from,
            to,
            cancellationToken);

        if (results is null)
            return NotFound(new { message = $"Organization '{organizationKey}' was not found." });

        return Ok(results);
    }
}
