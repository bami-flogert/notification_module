# Loggingbeleid

Applicatielogs mogen geen direct identificeerbare patiëntgegevens (PII) bevatten. Logs zijn bedoeld voor bedrijfsvoering en troubleshooting; patiëntnamen, telefoonnummers, e-mailadressen en ruwe FHIR-payloads worden alleen in de database bewaard zolang het retentiebeleid dat toestaat — niet in logstreams.

## Toegestane velden

Gebruik alleen niet-identificerende correlatie- en operationele velden in logberichten en gestructureerde logeigenschappen:

| Veld | Voorbeeld | Waar gebruikt |
|------|-----------|---------------|
| `AppointmentUuid` | `apt-001` | Producer-intake, consumer-dispatch |
| `OrganizationKey` | `demo-clinic` | Producer, consumer, providers |
| `ScheduledNotificationId` | GUID | Scheduler, consumer, providers |
| `Provider` / kanaalnaam | `SwiftSend` | Consumer-dispatch, provider-adapters |
| HTTP-statuscode | `200`, `502` | Provider-adapters na uitgaande aanroep |
| `ScheduledNotificationId` (fallback-keten) | GUID | Consumer-worker bij provider-fallback |
| Infrastructuur | RabbitMQ host/port, wachtrijnaam | Alleen bij connectie/bootstrap |
| Tellingen / booleans | `Created: true`, `Queued 3` | Scheduler, ingestion |

OpenTelemetry-span-tags volgen dezelfde regel: `appointment.uuid`, `organization.key`, `provider`, `scheduled_notification.id`, `dispatch.status`, enz.

## Verboden velden

Log deze nooit (ook niet als gestructureerde logargumenten of exception-tekst opgebouwd uit requestdata):

| Verboden | Reden |
|----------|-------|
| `PatientName` | Directe identificatie |
| `PatientPhone` | Directe identificatie |
| `PatientEmail` | Directe identificatie |
| `PatientUuid` | Patiëntreferentie (gebruik `AppointmentUuid` voor correlatie) |
| Ruwe FHIR JSON / request body | Kan al het bovenstaande bevatten |
| `RawSourcePayload` | Geserialiseerd afspraakbericht met PII |
| Provider request/response bodies | SMS/e-mailinhoud bevat patiëntnaam en telefoon |
| API-keys, JWT-tokens, client secrets | Credentials |

## Richtlijnen per component

### Producer — `OpenMrsWebhookController`

- Log afspraak-UUID en organizationsleutel bij intake.
- Log **geen** patiëntweergavenaam, telefoon, e-mail of de binnenkomende FHIR JSON-body.

### Producer — `AppointmentIngestionService`

- Log `AppointmentUuid`, `OrganizationKey` en of de rij is aangemaakt.
- Log geen geserialiseerd `AppointmentMessage` of `RawSourcePayload`.

### Consumer — provider-adapters

Elke adapter (`SwiftSendProvider`, `LegacyLinkProvider`, `SecurePostProvider`, `AsyncFlowProvider`) logt één regel per uitgaande HTTP-resultaat via `ProviderLogging.LogHttpResult`:

- Providernaam
- HTTP-statuscode
- `AppointmentUuid`
- `OrganizationKey`
- `ScheduledNotificationId`

Retry-waarschuwingen loggen alleen het aantal pogingen — geen message bodies.

### Consumer — `NotificationDispatcher`

- Log providernaam en `AppointmentUuid` bij start, succes en mislukking van verzending.
- Log geen exceptiondetails die provider-responsebodies bevatten.

## Handhaving

`tests/NotificationModule.Tests/Security/LogRedactionTests.cs` scant Producer- en Consumer-broncode op `LogInformation` / `LogError` (en verwante) aanroepen die verwijzen naar verboden PII-eigenschapsnamen. CI faalt als een overtreding wordt gevonden.

Bij nieuwe logregels: gebruik liever ondoorzichtige ID's en operationele metadata. Als je lokaal provider-payloads wilt debuggen, gebruik een debugger of tijdelijke logging achter een dev-only vlag — commit geen logregels met PII.

## Gerelateerd

- Issue #5 — log-redactie (backlog)
- Issue #3 — 14-dagen PII-purge (databaseretentie)
- `docs/madr/` ADR 0007 — OpenTelemetry-logs met Loki
