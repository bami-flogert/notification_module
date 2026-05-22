using System.Net.Mime;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace NotificationModule.Producer.Fhir;

internal static class FhirResponseWriter
{
    private static readonly FhirJsonSerializer Serializer = new(new SerializerSettings { Pretty = false });

    public static IActionResult FhirResult(Resource resource, int statusCode)
    {
        var json = Serializer.SerializeToString(resource);
        return new ContentResult
        {
            Content = json,
            ContentType = FhirConstants.FhirJsonMediaType,
            StatusCode = statusCode,
        };
    }

    public static IActionResult OperationOutcome(OperationOutcome outcome, int statusCode) =>
        FhirResult(outcome, statusCode);
}
