# Dashboard Database Guide

This module stores appointments, planned reminders, and delivery results in PostgreSQL. A future dashboard can read from these tables to show upcoming appointments, past appointments, upcoming notifications, and sent or failed notifications.

## Main Tables

### `organizations`

Represents an OpenMRS organization or hospital tenant.

Important columns:

- `Id`: internal organization ID.
- `Key`: stable organization key used by the API, for example `default` or `demo-hospital`.
- `Name`: display name.
- `TimeZone`: organization timezone.
- `IsEnabled`: whether this organization is active.

Use this table as the starting point for tenant filtering in dashboard queries.

### `provider_secrets`

Stores encrypted provider credentials per organization.

Important columns:

- `OrganizationId`: links to `organizations`.
- `Provider`: provider name, for example `SwiftSend`, `SecurePost`, `LegacyLink`, or `AsyncFlow`.
- `EncryptedPayload`: encrypted credential JSON.
- `Nonce`: AES-GCM nonce.

The dashboard should normally not display this table, except maybe showing which providers are configured for an organization. Never expose `EncryptedPayload` or `Nonce` in a UI.

### `appointments`

Stores appointment data received through the appointment intake endpoint.

Important columns:

- `Id`: internal appointment ID.
- `OrganizationId`: links to `organizations`.
- `AppointmentUuid`: OpenMRS appointment UUID.
- `PatientUuid`, `PatientName`, `PatientPhone`, `PatientEmail`: patient/contact fields needed for sending.
- `StartDateTime`: appointment start time in UTC.
- `Status`: appointment status, for example `Confirmed` or `Cancelled`.
- `Location`: appointment location.
- `Instructions`: patient instructions.
- `RawSourcePayload`: original source payload JSON for debugging/audit.

Appointments are unique per organization using `(OrganizationId, AppointmentUuid)`.

### `scheduled_notifications`

Stores planned reminder moments for appointments.

Important columns:

- `Id`: internal scheduled notification ID.
- `OrganizationId`: links to `organizations`.
- `AppointmentId`: links to `appointments`.
- `ReminderType`: reminder type, currently `24h` or `1h`.
- `ScheduledSendAt`: when the scheduler should publish the notification.
- `Status`: notification lifecycle status.

Current statuses:

- `Pending`: waiting until `ScheduledSendAt`.
- `Publishing`: claimed by the scheduler for outbound publish (short-lived).
- `Queued`: published to RabbitMQ, waiting for provider processing.
- `Sent`: all provider deliveries succeeded.
- `Failed`: one or more provider deliveries failed.
- `Cancelled`: appointment was cancelled or changed before sending.

### `notification_deliveries`

Stores the delivery result per provider for each scheduled notification.

Important columns:

- `Id`: internal delivery ID.
- `OrganizationId`: links to `organizations`.
- `AppointmentId`: links to `appointments`.
- `ScheduledNotificationId`: links to `scheduled_notifications`.
- `Provider`: provider name.
- `Status`: `Sent` or `Failed`.
- `SentAt`: timestamp for successful delivery.
- `FailedAt`: timestamp for failed delivery.
- `ErrorMessage`: failure reason if available.

Each scheduled notification can have multiple delivery rows, one per provider.

## Relationship Overview

```mermaid
erDiagram
    organizations ||--o{ provider_secrets : has
    organizations ||--o{ appointments : owns
    appointments ||--o{ scheduled_notifications : schedules
    scheduled_notifications ||--o{ notification_deliveries : records
    appointments ||--o{ notification_deliveries : summarizes
```

## Dashboard Views

### Upcoming Appointments

Appointments with a start time in the future.

```sql
select
  o."Key" as organization,
  a."AppointmentUuid",
  a."PatientUuid",
  a."StartDateTime",
  a."Status",
  a."Location"
from appointments a
join organizations o on o."Id" = a."OrganizationId"
where a."StartDateTime" > now()
order by a."StartDateTime";
```

### Past Appointments

Appointments that have already started.

```sql
select
  o."Key" as organization,
  a."AppointmentUuid",
  a."PatientUuid",
  a."StartDateTime",
  a."Status"
from appointments a
join organizations o on o."Id" = a."OrganizationId"
where a."StartDateTime" <= now()
order by a."StartDateTime" desc;
```

### Upcoming Notifications

Pending reminders that have not been sent yet.

```sql
select
  o."Key" as organization,
  a."AppointmentUuid",
  a."PatientUuid",
  sn."ReminderType",
  sn."ScheduledSendAt",
  sn."Status"
from scheduled_notifications sn
join appointments a on a."Id" = sn."AppointmentId"
join organizations o on o."Id" = sn."OrganizationId"
where sn."Status" = 'Pending'
order by sn."ScheduledSendAt";
```

### Sent And Failed Notifications

Provider-level delivery history.

```sql
select
  o."Key" as organization,
  a."AppointmentUuid",
  sn."ReminderType",
  d."Provider",
  d."Status",
  d."SentAt",
  d."FailedAt",
  d."ErrorMessage"
from notification_deliveries d
join scheduled_notifications sn on sn."Id" = d."ScheduledNotificationId"
join appointments a on a."Id" = d."AppointmentId"
join organizations o on o."Id" = d."OrganizationId"
order by d."UpdatedAt" desc;
```

### Recent Delivery Failures (last 1h)

Operational error oversight for administrators (no patient names). Used by the provisioned Grafana panel **Recent Delivery Failures (last 1h)**.

```sql
select
  d."FailedAt" as failed_at,
  o."Key" as organization,
  a."AppointmentUuid" as appointment_uuid,
  d."Provider" as provider,
  left(d."ErrorMessage", 500) as error_message
from notification_deliveries d
join scheduled_notifications sn on sn."Id" = d."ScheduledNotificationId"
join appointments a on a."Id" = d."AppointmentId"
join organizations o on o."Id" = d."OrganizationId"
where d."Status" = 'Failed'
  and d."FailedAt" >= now() - interval '1 hour'
order by d."FailedAt" desc
limit 50;
```

## Suggested Dashboard Filters

- Organization: filter by `organizations.Key`.
- Appointment period: filter `appointments.StartDateTime`.
- Notification status: filter `scheduled_notifications.Status`.
- Provider status: filter `notification_deliveries.Provider` and `notification_deliveries.Status`.

## Privacy Notes

The `appointments` table stores patient-identifiable fields (`PatientName`, `PatientPhone`, `PatientEmail`) for outbound messaging. Default admin dashboard SQL in this repo and the provisioned Grafana **Notification Module** dashboard use **`PatientUuid`** and **`AppointmentUuid` only**—not names or contact fields.

Restrict dashboard access per organization. For billing/audit views, prefer `notification_deliveries` joined with organization, provider, status, and timestamps. Expose name, phone, email, instructions, or `RawSourcePayload` only in roles that explicitly require clinical appointment detail.
