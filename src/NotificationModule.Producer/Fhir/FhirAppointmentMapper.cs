using Hl7.Fhir.Model;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Fhir;

public sealed class FhirAppointmentMapper
{
    public AppointmentMessage ToAppointmentMessage(Appointment appointment, string? organizationKeyOverride)
    {
        var appointmentUuid = appointment.Identifier?
            .First(i => i.System == FhirConstants.OpenMrsAppointmentIdentifierSystem)
            .Value!;

        var patientParticipant = appointment.Participant!
            .First(p => p.Actor?.Reference?.StartsWith("Patient/", StringComparison.OrdinalIgnoreCase) == true);

        var patientRef = patientParticipant.Actor!.Reference!;
        var patientUuid = patientRef["Patient/".Length..];

        var orgKey = organizationKeyOverride
            ?? GetExtensionString(appointment, FhirConstants.OrganizationKeyExtensionUrl)
            ?? string.Empty;

        return new AppointmentMessage
        {
            AppointmentUuid = appointmentUuid,
            PatientUuid = patientUuid,
            PatientName = patientParticipant.Actor.Display ?? string.Empty,
            PatientPhone = GetExtensionString(appointment, FhirConstants.PatientPhoneExtensionUrl) ?? string.Empty,
            PatientEmail = GetExtensionString(appointment, FhirConstants.PatientEmailExtensionUrl) ?? string.Empty,
            StartDateTime = appointment.Start!.Value.UtcDateTime,
            Status = MapStatusToLegacy(appointment.Status!.Value),
            OrganizationKey = orgKey,
            Location = GetExtensionString(appointment, FhirConstants.LocationTextExtensionUrl)
                ?? appointment.Description
                ?? string.Empty,
            Instructions = appointment.PatientInstruction ?? string.Empty,
        };
    }

    public Appointment ToFhirAppointment(AppointmentMessage message, string organizationKey)
    {
        var status = MapLegacyToFhirStatus(message.Status);

        var appointment = new Appointment
        {
            Identifier =
            [
                new Identifier
                {
                    System = FhirConstants.OpenMrsAppointmentIdentifierSystem,
                    Value = message.AppointmentUuid,
                },
            ],
            Status = status,
            Start = new DateTimeOffset(
                DateTime.SpecifyKind(message.StartDateTime, DateTimeKind.Utc)),
            Participant =
            [
                new Appointment.ParticipantComponent
                {
                    Actor = new ResourceReference($"Patient/{message.PatientUuid}")
                    {
                        Display = message.PatientName,
                    },
                    Status = ParticipationStatus.Accepted,
                },
            ],
            PatientInstruction = string.IsNullOrWhiteSpace(message.Instructions) ? null : message.Instructions,
            Description = string.IsNullOrWhiteSpace(message.Location) ? null : message.Location,
            Extension = BuildExtensions(message, organizationKey),
        };

        return appointment;
    }

    private static List<Extension> BuildExtensions(AppointmentMessage message, string organizationKey)
    {
        var extensions = new List<Extension>
        {
            new(FhirConstants.OrganizationKeyExtensionUrl, new FhirString(organizationKey)),
        };

        if (!string.IsNullOrWhiteSpace(message.PatientPhone))
            extensions.Add(new Extension(FhirConstants.PatientPhoneExtensionUrl, new FhirString(message.PatientPhone)));

        if (!string.IsNullOrWhiteSpace(message.PatientEmail))
            extensions.Add(new Extension(FhirConstants.PatientEmailExtensionUrl, new FhirString(message.PatientEmail)));

        if (!string.IsNullOrWhiteSpace(message.Location))
            extensions.Add(new Extension(FhirConstants.LocationTextExtensionUrl, new FhirString(message.Location)));

        return extensions;
    }

    private static string? GetExtensionString(DomainResource resource, string url) =>
        resource.Extension?
            .FirstOrDefault(e => e.Url == url)?
            .Value is FhirString s
            ? s.Value
            : null;

    private static string MapStatusToLegacy(Appointment.AppointmentStatus status) =>
        status == Appointment.AppointmentStatus.Cancelled ? "Cancelled" : "Confirmed";

    private static Appointment.AppointmentStatus MapLegacyToFhirStatus(string status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase)
            ? Appointment.AppointmentStatus.Cancelled
            : Appointment.AppointmentStatus.Booked;
}
