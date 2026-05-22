using Hl7.Fhir.Model;
using NotificationModule.Producer.Fhir;
using NotificationModule.Shared.Models;

namespace NotificationModule.Tests.Producer;

public sealed class FhirAppointmentMapperTests
{
    private readonly FhirAppointmentMapper _mapper = new();

    [Fact]
    public void ToAppointmentMessage_maps_identifier_patient_and_start()
    {
        var fhir = new Appointment
        {
            Identifier =
            [
                new Identifier
                {
                    System = FhirConstants.OpenMrsAppointmentIdentifierSystem,
                    Value = "appt-123",
                },
            ],
            Status = Appointment.AppointmentStatus.Booked,
            Start = DateTimeOffset.UtcNow.AddDays(1),
            Participant =
            [
                new Appointment.ParticipantComponent
                {
                    Actor = new ResourceReference("Patient/patient-456") { Display = "Jane Doe" },
                    Status = ParticipationStatus.Accepted,
                },
            ],
            PatientInstruction = "Fast before visit",
            Extension =
            [
                new Extension(FhirConstants.PatientPhoneExtensionUrl, new FhirString("+31600000000")),
                new Extension(FhirConstants.PatientEmailExtensionUrl, new FhirString("jane@example.com")),
                new Extension(FhirConstants.LocationTextExtensionUrl, new FhirString("Room 12")),
            ],
        };

        var message = _mapper.ToAppointmentMessage(fhir, "demo-hospital");

        Assert.Equal("appt-123", message.AppointmentUuid);
        Assert.Equal("patient-456", message.PatientUuid);
        Assert.Equal("Jane Doe", message.PatientName);
        Assert.Equal("+31600000000", message.PatientPhone);
        Assert.Equal("jane@example.com", message.PatientEmail);
        Assert.Equal("Room 12", message.Location);
        Assert.Equal("Fast before visit", message.Instructions);
        Assert.Equal("Confirmed", message.Status);
    }

    [Fact]
    public void ToFhirAppointment_round_trips_core_fields()
    {
        var message = new AppointmentMessage
        {
            AppointmentUuid = "appt-99",
            PatientUuid = "patient-1",
            PatientName = "Test",
            PatientPhone = "+31123456789",
            PatientEmail = "t@example.com",
            StartDateTime = DateTime.UtcNow.AddDays(3),
            Status = "Confirmed",
            Location = "Clinic A",
            Instructions = "Bring ID",
        };

        var fhir = _mapper.ToFhirAppointment(message, "default");

        Assert.Equal("Appointment", fhir.TypeName);
        Assert.Equal(Appointment.AppointmentStatus.Booked, fhir.Status);
        Assert.Equal("appt-99", fhir.Identifier!.Single(i => i.System == FhirConstants.OpenMrsAppointmentIdentifierSystem).Value);
        Assert.Equal("Patient/patient-1", fhir.Participant!.Single().Actor!.Reference);
    }
}
