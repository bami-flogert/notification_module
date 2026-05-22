using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.Fhir;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("fhir")]
public sealed class FhirMetadataController : ControllerBase
{
    [HttpGet("metadata")]
    [Produces(FhirConstants.FhirJsonMediaType)]
    public IActionResult GetMetadata()
    {
        var capability = new CapabilityStatement
        {
            Status = PublicationStatus.Active,
            Date = DateTimeOffset.UtcNow.ToString("o"),
            Software = new CapabilityStatement.SoftwareComponent
            {
                Name = "NotificationModule.Producer",
            },
            FhirVersion = FHIRVersion.N4_0_1,
            Format = ["application/fhir+json"],
            Rest =
            [
                new CapabilityStatement.RestComponent
                {
                    Mode = CapabilityStatement.RestfulCapabilityMode.Server,
                    Resource =
                    [
                        new CapabilityStatement.ResourceComponent
                        {
                            Type = "Appointment",
                            Interaction =
                            [
                                new CapabilityStatement.ResourceInteractionComponent
                                {
                                    Code = CapabilityStatement.TypeRestfulInteraction.Create,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        return FhirResponseWriter.FhirResult(capability, StatusCodes.Status200OK);
    }
}
