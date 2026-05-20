# Appointment Intake Endpoint

Appointments are stored in PostgreSQL first. Posting an appointment does not send notifications immediately. The endpoint saves the appointment for an organization and creates pending reminder rows. The scheduler publishes a reminder to RabbitMQ when its `ScheduledSendAt` time is due.

## Endpoint

```http
POST /api/appointments
POST /api/appointments/{organizationKey}
```

## Authentication

Requests must include an API key header:

- `X-Api-Key: <per-organization api key>`

The local demo seeds a default API key from `APIKEY_SEED_DEFAULT` (see `env.example`). In production, rotate keys and never store plaintext keys in code.

The organization can be supplied in one of three ways:

- Route: `POST /api/appointments/demo-hospital`
- Header: `X-Organization-Key: demo-hospital`
- Body: `"organizationKey": "demo-hospital"`

If none is supplied, the service uses the configured default organization from `Organizations:Default:Key`.

## Example Request

```bash
curl -X POST http://localhost:5001/api/appointments/demo-hospital \
  -H "X-Api-Key: change-me-in-prod" \
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

The endpoint creates rows in `scheduled_notifications`:

- `24h`, scheduled 24 hours before `startDateTime`
- `1h`, scheduled 1 hour before `startDateTime`

Reminder rows are only created when their scheduled send time is still in the future. If the appointment already started, no pending notifications are created.

The scheduler runs inside the producer. It atomically claims `Pending` rows where `ScheduledSendAt <= now` (PostgreSQL `FOR UPDATE SKIP LOCKED`), publishes each message to RabbitMQ, and only then marks the row as `Queued`. Failed publishes revert the row to `Pending` for retry.

The scheduler publishes each message to **one** provider queue based on the organization’s configured provider policy. The consumer writes one delivery row per attempted provider to `notification_deliveries`.

If a provider attempt fails, the consumer republishes the message to the next fallback provider (if configured). The scheduled notification is marked:

- `Sent` when **any** provider succeeds
- `Failed` only when the preferred+fallback chain is exhausted

When an existing appointment is updated, old pending notifications are marked `Cancelled` and new future pending notifications are created. When the appointment status is `Cancelled` or `Canceled`, all pending notifications are cancelled.

## Provider Secrets Per Organization

Provider secrets remain encrypted in `provider_secrets`, but they are now scoped by organization using `(organization_id, provider)`. The local demo seeds secrets for the default organization. The scheduler includes the organization key in the RabbitMQ message so the delivery rows can be tied back to the right organization.

## Testing a Due Notification

For a normal appointment far in the future, the scheduler will wait until 24 hours or 1 hour before the appointment. To test actual sending immediately, create an appointment a little more than 1 hour in the future. The `1h` reminder will be due within a few minutes.

Example: if the current UTC time is `10:00`, post an appointment with `startDateTime` around `11:01`.

Then watch the logs:

```bash
docker compose --env-file env.example logs -f producer consumer
```

Useful database checks:

```sql
select "AppointmentUuid", "StartDateTime", "Status"
from appointments
order by "CreatedAt" desc;

select "ReminderType", "ScheduledSendAt", "Status"
from scheduled_notifications
order by "ScheduledSendAt";

select d."Provider", d."Status", d."SentAt", d."FailedAt", d."ErrorMessage"
from notification_deliveries d
join scheduled_notifications sn on sn."Id" = d."ScheduledNotificationId"
join appointments a on a."Id" = sn."AppointmentId"
where a."AppointmentUuid" = 'openmrs-appointment-123'
order by d."Provider";
```
