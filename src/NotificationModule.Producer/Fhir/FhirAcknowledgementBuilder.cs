using Hl7.Fhir.Model;

namespace NotificationModule.Producer.Fhir;

internal static class FhirAcknowledgementBuilder
{
    public static OperationOutcome Success(string diagnostics, string? appointmentUuid = null)
    {
        var detail = appointmentUuid is null
            ? diagnostics
            : $"{diagnostics} Appointment: {appointmentUuid}.";

        return new OperationOutcome
        {
            Id = Guid.NewGuid().ToString("N"),
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Information,
                    Code = OperationOutcome.IssueType.Informational,
                    Diagnostics = detail,
                },
            ],
        };
    }

    public static OperationOutcome Error(
        string diagnostics,
        OperationOutcome.IssueType code = OperationOutcome.IssueType.Invalid)
    {
        return new OperationOutcome
        {
            Id = Guid.NewGuid().ToString("N"),
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = code,
                    Diagnostics = diagnostics,
                },
            ],
        };
    }

    public static OperationOutcome FromException(Exception ex) =>
        ex switch
        {
            ArgumentException => Error(ex.Message, OperationOutcome.IssueType.Required),
            InvalidOperationException => Error(ex.Message, OperationOutcome.IssueType.Forbidden),
            _ => Error(ex.Message),
        };
}
