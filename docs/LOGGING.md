# Logging policy

Application logs must not contain directly identifiable patient data (PII). Logs exist for operations and troubleshooting; patient names, phone numbers, email addresses, and raw FHIR payloads are stored in the database only for as long as retention policy allows—not in log streams.

## Allowed fields

Use only non-identifying correlation and operational fields in log messages and structured log properties:

| Field | Example | Where used |
|-------|---------|------------|
| `AppointmentUuid` | `apt-001` | Producer intake, consumer dispatch |
| `OrganizationKey` | `demo-clinic` | Producer, consumer, providers |
| `ScheduledNotificationId` | GUID | Scheduler, consumer, providers |
| `Provider` / channel name | `SwiftSend` | Consumer dispatch, provider adapters |
| HTTP status code | `200`, `502` | Provider adapters after outbound call |
| `ScheduledNotificationId` (fallback chain) | GUID | Consumer worker on provider fallback |
| Infrastructure | RabbitMQ host/port, queue name | Connection/bootstrap only |
| Counts / booleans | `Created: true`, `Queued 3` | Scheduler, ingestion |

OpenTelemetry span tags follow the same rule: `appointment.uuid`, `organization.key`, `provider`, `scheduled_notification.id`, `dispatch.status`, etc.

## Forbidden fields

Never log these (including as structured log arguments or exception message text built from request data):

| Forbidden | Reason |
|-----------|--------|
| `PatientName` | Direct identifier |
| `PatientPhone` | Direct identifier |
| `PatientEmail` | Direct identifier |
| `PatientUuid` | Patient reference (use `AppointmentUuid` for correlation) |
| Raw FHIR JSON / request body | May contain all of the above |
| `RawSourcePayload` | Serialized appointment message with PII |
| Provider request/response bodies | SMS/email content includes patient name and phone |
| API keys, JWT tokens, client secrets | Credentials |

## Component guidelines

### Producer — `FhirAppointmentController`

- Log appointment UUID and organization key on intake.
- Do **not** log patient display name, phone, email, or the incoming FHIR JSON body.

### Producer — `AppointmentIngestionService`

- Log `AppointmentUuid`, `OrganizationKey`, and whether the row was created.
- Do not log serialized `AppointmentMessage` or `RawSourcePayload`.

### Consumer — provider adapters

Each adapter (`SwiftSendProvider`, `LegacyLinkProvider`, `SecurePostProvider`, `AsyncFlowProvider`) logs one line per outbound HTTP result via `ProviderLogging.LogHttpResult`:

- Provider name
- HTTP status code
- `AppointmentUuid`
- `OrganizationKey`
- `ScheduledNotificationId`

Retry warnings log attempt count only—no message bodies.

### Consumer — `NotificationDispatcher`

- Log provider name and `AppointmentUuid` on send start, success, and failure.
- Do not log exception details that embed provider response bodies.

## Enforcement

`tests/NotificationModule.Tests/Security/LogRedactionTests.cs` scans Producer and Consumer source for `LogInformation` / `LogError` (and related) calls that reference forbidden PII property names. CI fails if a violation is found.

When adding new log statements, prefer opaque IDs and operational metadata. If you need to debug provider payloads locally, use a debugger or temporary logging behind a dev-only flag—do not commit PII-bearing log lines.

## Related

- Issue #5 — log redaction (assignment backlog)
- Issue #3 — 14-day PII purge (database retention)
- `docs/madr/` ADR 0007 — OpenTelemetry logs with Loki
