# Appointment Intake Endpoint

Appointments are stored in PostgreSQL first. Posting an appointment does not send notifications immediately and does not publish to RabbitMQ. The endpoint saves the appointment for an organization and creates pending reminder rows that a later scheduler can send 24 hours and 1 hour before the appointment.

## Endpoint

```http
POST /api/appointments
POST /api/appointments/{organizationKey}
```

The organization can be supplied in one of three ways:

- Route: `POST /api/appointments/demo-hospital`
- Header: `X-Organization-Key: demo-hospital`
- Body: `"organizationKey": "demo-hospital"`

If none is supplied, the service uses the configured default organization from `Organizations:Default:Key`.

## Example Request

```bash
curl -X POST http://localhost:5001/api/appointments/demo-hospital \
  -H "Content-Type: application/json" \
  -d '{
    "appointmentUuid": "openmrs-appointment-123",
    "patientUuid": "openmrs-patient-456",
    "patientName": "Peter Jansen",
    "patientPhone": "+31612345678",
    "patientEmail": "peter.jansen@example.com",
    "startDateTime": "2026-05-20T14:30:00Z",
    "status": "Confirmed",
    "location": "Polikliniek A, kamer 12",
    "instructions": "Neem een geldig identiteitsbewijs mee."
  }'
```

## Example Response

```json
{
  "message": "Appointment saved.",
  "appointmentUuid": "openmrs-appointment-123",
  "organizationKey": "demo-hospital",
  "pendingNotifications": 2
}
```

## Database Behavior

The endpoint creates or updates the organization in `organizations` and stores the appointment in `appointments`.

Appointments are unique per organization, using `(organization_id, appointment_uuid)`. This allows two OpenMRS organizations to use the same appointment UUID without sharing data.

For future sending, the endpoint creates rows in `scheduled_notifications`:

- `24h`, scheduled 24 hours before `startDateTime`
- `1h`, scheduled 1 hour before `startDateTime`

Reminder rows are only created when their scheduled send time is still in the future. If the appointment already started, no pending notifications are created.

When an existing appointment is updated, old pending notifications are marked `Cancelled` and new future pending notifications are created. When the appointment status is `Cancelled` or `Canceled`, all pending notifications are cancelled.

## Provider Secrets Per Organization

Provider secrets remain encrypted in `provider_secrets`, but they are now scoped by organization using `(organization_id, provider)`. The local demo seeds secrets for the default organization. Later, when scheduled notifications are published to RabbitMQ, the organization context can be included so the consumer can load the correct provider credentials for that organization.
