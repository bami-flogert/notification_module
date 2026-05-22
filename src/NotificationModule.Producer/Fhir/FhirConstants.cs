namespace NotificationModule.Producer.Fhir;

public static class FhirConstants
{
    public const string FhirJsonMediaType = "application/fhir+json";
    public const string OpenMrsAppointmentIdentifierSystem = "http://openmrs.org/appointment";
    public const string OrganizationKeyExtensionUrl = "http://notification-module.local/StructureDefinition/organization-key";
    public const string PatientPhoneExtensionUrl = "http://notification-module.local/StructureDefinition/patient-phone";
    public const string PatientEmailExtensionUrl = "http://notification-module.local/StructureDefinition/patient-email";
    public const string LocationTextExtensionUrl = "http://notification-module.local/StructureDefinition/location-text";
}
