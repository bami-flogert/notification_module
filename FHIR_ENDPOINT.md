# FHIR Appointment Intake

Primary integration endpoint for OpenMRS and other FHIR-aware clients.

## Endpoints

| Method | Path | Content-Type |
|--------|------|----------------|
| `POST` | `/fhir/Appointment` | `application/fhir+json` |
| `POST` | `/fhir/Appointment/{organizationKey}` | `application/fhir+json` |
| `GET` | `/fhir/metadata` | `application/fhir+json` (CapabilityStatement) |

Authentication matches the legacy endpoint: `X-Api-Key` and organization via route, `X-Organization-Key`, or extension.

## Character encoding

Only **UTF-8** is supported. Use `Content-Type: application/fhir+json` or `application/fhir+json; charset=utf-8`. Other charsets (e.g. `iso-8859-1`) return HTTP `400` with an `OperationOutcome`. Invalid UTF-8 bytes in the body are rejected the same way. See [`docs/EXTENSIBILITY.md`](docs/EXTENSIBILITY.md) (section *Tekenset*).

## Required fields

- `resourceType`: `Appointment`
- `status`: FHIR [AppointmentStatus](https://hl7.org/fhir/R4/valueset-appointmentstatus.html) (e.g. `booked`, `cancelled`)
- `start`: appointment start instant
- `identifier`: at least one with system `http://openmrs.org/appointment` (OpenMRS appointment UUID)
- `participant`: at least one with `actor.reference` = `Patient/{uuid}`

## Optional extensions

| URL | Value |
|-----|--------|
| `http://notification-module.local/StructureDefinition/organization-key` | Tenant key when not in route/header |
| `http://notification-module.local/StructureDefinition/patient-phone` | SMS destination |
| `http://notification-module.local/StructureDefinition/patient-email` | Email destination |
| `http://notification-module.local/StructureDefinition/location-text` | Location line in notification |

Also supported: `patientInstruction`, `description` (location fallback).

## Example request

```bash
curl -X POST "http://localhost:5001/fhir/Appointment/default" \
  -H "X-Api-Key: change-me-in-prod" \
  -H "Content-Type: application/fhir+json" \
  -H "Accept: application/fhir+json" \
  -d '{
    "resourceType": "Appointment",
    "status": "booked",
    "start": "2026-05-20T14:30:00Z",
    "identifier": [{
      "system": "http://openmrs.org/appointment",
      "value": "openmrs-appointment-123"
    }],
    "participant": [{
      "actor": { "reference": "Patient/openmrs-patient-456", "display": "Peter Jansen" },
      "status": "accepted"
    }],
    "patientInstruction": "Neem een geldig identiteitsbewijs mee.",
    "extension": [
      { "url": "http://notification-module.local/StructureDefinition/patient-phone", "valueString": "+31612345678" },
      { "url": "http://notification-module.local/StructureDefinition/patient-email", "valueString": "peter@example.com" },
      { "url": "http://notification-module.local/StructureDefinition/location-text", "valueString": "Polikliniek A, kamer 12" }
    ]
  }'
```

## ACK (success)

Per [ADR 0010](docs/madr/0010-fhir-integratie.md): the appointment is stored synchronously in PostgreSQL, so the API returns **`201 Created`** (new) or **`200 OK`** (update)—not `202 Accepted`.

Response body: a `Bundle` containing:

1. The persisted `Appointment` resource
2. An `OperationOutcome` with `severity=information` confirming receipt

`Location` header: `/fhir/Appointment/{appointmentUuid}`

## ACK (failure)

HTTP `400` / `401` / `403` / `422` with `OperationOutcome` (`severity=error`, `diagnostics` describes the problem).

## Legacy endpoint

`POST /api/appointments` remains available but is **deprecated**. Prefer FHIR for new integrations. See [`APPOINTMENT_ENDPOINT.md`](APPOINTMENT_ENDPOINT.md).
