using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.Fhir;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("fhir")]
[ServiceFilter(typeof(AppointmentApiKeyAuthFilter))]
public sealed class FhirAppointmentController : ControllerBase
{
    private static readonly FhirJsonParser Parser = new();

    private readonly AppointmentIngestionService _ingestionService;
    private readonly FhirAppointmentMapper _mapper;
    private readonly FhirAppointmentValidator _validator;
    private readonly ILogger<FhirAppointmentController> _logger;

    public FhirAppointmentController(
        AppointmentIngestionService ingestionService,
        FhirAppointmentMapper mapper,
        FhirAppointmentValidator validator,
        ILogger<FhirAppointmentController> logger)
    {
        _ingestionService = ingestionService;
        _mapper = mapper;
        _validator = validator;
        _logger = logger;
    }

    [HttpPost("Appointment")]
    [HttpPost("Appointment/{organizationKey}")]
    [Consumes(FhirConstants.FhirJsonMediaType)]
    [Produces(FhirConstants.FhirJsonMediaType)]
    public async Task<IActionResult> PostAppointment(
        string? organizationKey,
        [FromHeader(Name = "X-Organization-Key")] string? organizationHeader,
        CancellationToken cancellationToken)
    {
        if (!FhirRequestEncoding.IsSupportedContentType(Request.ContentType, out var charsetError))
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error(charsetError!),
                StatusCodes.Status400BadRequest);
        }

        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory, cancellationToken);
        var body = memory.ToArray();

        if (body.Length == 0)
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error("Request body is required."),
                StatusCodes.Status400BadRequest);
        }

        if (!FhirRequestEncoding.TryDecodeUtf8Body(body, out var json, out var utf8Error))
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error(utf8Error!),
                StatusCodes.Status400BadRequest);
        }

        Resource resource;
        try
        {
            resource = Parser.Parse<Resource>(json);
        }
        catch (FormatException ex)
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error($"Invalid FHIR JSON: {ex.Message}"),
                StatusCodes.Status400BadRequest);
        }

        if (resource is not Appointment appointment)
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error("Body must be a FHIR Appointment resource."),
                StatusCodes.Status400BadRequest);
        }

        var validationErrors = _validator.Validate(appointment);
        if (validationErrors.Count > 0)
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error(string.Join(" ", validationErrors)),
                StatusCodes.Status400BadRequest);
        }

        var resolvedOrgKey = organizationKey ?? organizationHeader;
        AppointmentMessage message;
        try
        {
            message = _mapper.ToAppointmentMessage(appointment, resolvedOrgKey);
        }
        catch (Exception ex)
        {
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.Error(ex.Message),
                StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation(
            "Received FHIR appointment {AppointmentUuid} for organization {OrganizationKey}",
            message.AppointmentUuid,
            message.OrganizationKey);

        AppointmentIngestionResult result;
        try
        {
            result = await _ingestionService.IngestAsync(
                message,
                resolvedOrgKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var status = ex is InvalidOperationException
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status400BadRequest;
            return FhirResponseWriter.OperationOutcome(
                FhirAcknowledgementBuilder.FromException(ex),
                status);
        }

        var savedAppointment = _mapper.ToFhirAppointment(
            message with { OrganizationKey = result.OrganizationKey },
            result.OrganizationKey);

        var ack = FhirAcknowledgementBuilder.Success(
            $"Appointment saved. Pending notifications: {result.PendingNotificationCount}.",
            result.AppointmentUuid);

        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Collection,
            Entry =
            [
                new Bundle.EntryComponent { Resource = savedAppointment },
                new Bundle.EntryComponent { Resource = ack },
            ],
        };

        var statusCode = result.Created
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

        Response.Headers.Location = $"/fhir/Appointment/{result.AppointmentUuid}";
        return FhirResponseWriter.FhirResult(bundle, statusCode);
    }
}
