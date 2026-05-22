using Hl7.Fhir.Model;

namespace NotificationModule.Producer.Fhir;

public sealed class FhirAppointmentValidator
{
    public IReadOnlyList<string> Validate(Appointment appointment)
    {
        var errors = new List<string>();

        if (!string.Equals(appointment.TypeName, "Appointment", StringComparison.Ordinal))
            errors.Add("Resource must be an Appointment.");

        if (appointment.Status is null)
            errors.Add("Appointment.status is required.");
        else if (!IsSupportedStatus(appointment.Status.Value))
            errors.Add($"Appointment.status '{appointment.Status}' is not supported.");

        if (appointment.Start is null)
            errors.Add("Appointment.start is required.");

        var patientParticipant = appointment.Participant?
            .FirstOrDefault(p =>
                p.Actor?.Reference is not null
                && p.Actor.Reference.StartsWith("Patient/", StringComparison.OrdinalIgnoreCase));

        if (patientParticipant is null)
            errors.Add("Appointment.participant must include a Patient reference.");

        var identifier = appointment.Identifier?
            .FirstOrDefault(i => i.System == FhirConstants.OpenMrsAppointmentIdentifierSystem);
        if (identifier is null || string.IsNullOrWhiteSpace(identifier.Value))
            errors.Add($"Appointment.identifier with system '{FhirConstants.OpenMrsAppointmentIdentifierSystem}' is required.");

        return errors;
    }

    private static bool IsSupportedStatus(Appointment.AppointmentStatus status) =>
        status is Appointment.AppointmentStatus.Booked
            or Appointment.AppointmentStatus.Cancelled
            or Appointment.AppointmentStatus.Pending
            or Appointment.AppointmentStatus.Proposed
            or Appointment.AppointmentStatus.Arrived
            or Appointment.AppointmentStatus.Fulfilled
            or Appointment.AppointmentStatus.Noshow
            or Appointment.AppointmentStatus.CheckedIn
            or Appointment.AppointmentStatus.Waitlist
            or Appointment.AppointmentStatus.EnteredInError;
}
