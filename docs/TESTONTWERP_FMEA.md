# Testontwerp op basis van FMEA

Dit document koppelt FMEA-failure modes aan concrete tests en demo-scenario's. Bewijs van uitvoering: unit/integration tests in `tests/`, scripts in `scripts/`, en [TESTRAPPORT.md](TESTRAPPORT.md).

## Overzicht

| Test-ID | FMEA-onderdeel | Type | Automatisering | Status |
|---------|----------------|------|----------------|--------|
| T-OMOD-01 | Producer down (OMOD) | Integratie / demo | Handmatig + outbox-doc | Ontwerp |
| T-OMOD-02 | Dubbele webhook | Unit + API | `OpenMrsWebhookApiTests.Post_openmrs_webhook_duplicate_*`, `AppointmentIngestionServiceTests` | Geïmplementeerd |
| T-OMOD-03 | Cancel na create | API | `OpenMrsWebhookApiTests.Post_openmrs_webhook_cancelled_*` | Geïmplementeerd |
| T-OMOD-04 | Ontbrekende contactgegevens | Unit | `OpenMrsWebhookMapperTests` + delivery failure pad | Gedeeltelijk |
| T-OMOD-05 | Ongeldige payload | API | `OpenMrsWebhookMapperTests` + 400-responses | Geïmplementeerd |
| T-PROD-01 | Ongeldige datum | Unit | `AppointmentIngestionServiceTests` | Bestaand |
| T-PROD-02 | Dubbele intake | Unit | `AppointmentIngestionServiceTests` update-pad | Bestaand |
| T-SCHED-01 | Scheduler publish failure | Unit | `NotificationSchedulerReadinessTests` | Bestaand |
| T-RMQ-01 | RabbitMQ down | Integratie | `comprehensive-test.sh` health/ready | Bestaand |
| T-CONS-01 | Provider failure + fallback | Unit/integratie | Consumer tests + `RELIABILITY.md` | Bestaand |

## Detail per prioriteit (demo + FMEA)

### T-OMOD-01 — Producer tijdelijk down (resiliency)

**FMEA:** OpenMRS OMOD — Producer endpoint down.

**Stappen (demo):**

1. Stop Producer-container: `docker compose stop producer`
2. Maak afspraak in OpenMRS O3 UI (of simuleer met curl naar outbox-only stub)
3. Controleer OMOD outbox: rij `PENDING`
4. Start Producer: `docker compose start producer`
5. Outbox-job levert alsnog; controleer `appointments` in PostgreSQL

**Acceptatie:** geen verloren events; webhook komt binnen binnen geconfigureerde retry-window.

### T-OMOD-02 — Idempotency (dubbele CREATED)

**FMEA:** Dubbele webhook.

**Stappen:**

1. POST dezelfde `OpenMrsAppointmentWebhook` twee keer naar `/api/webhooks/openmrs/appointments/default`
2. Query `scheduled_notifications`: maximaal 2 pending rijen (24h + 1h), geen duplicaten per `ReminderType`

**Automatisering:** uitbreiden `OpenMrsWebhookApiTests` met dubbele POST (optioneel vervolg).

### T-OMOD-03 — Cancel flow

**FMEA:** Event-volgorde / cancel.

**Stappen:**

1. POST `event: CREATED` → verwacht `pendingNotifications: 2`
2. POST `event: CANCELLED` → verwacht `pendingNotifications: 0`
3. DB: pending status = `Cancelled`

**Automatisering:** `OpenMrsWebhookApiTests.Post_openmrs_webhook_cancelled_clears_pending_reminders`

### T-OMOD-04 — Update starttijd

**FMEA:** Update vóór reminder verzonden.

**Stappen:**

1. POST CREATED met `startDateTime` over 48u
2. POST UPDATED met `startDateTime` over 72u
3. Oude pending reminders `Cancelled`; nieuwe met herberekende `ScheduledSendAt`

**Automatisering:** `AppointmentIngestionServiceTests` update-scenario's (bestaand).

### Contracttest — OMOD payload

**Payload (vast contract):**

```json
{
  "event": "CREATED",
  "appointmentUuid": "…",
  "status": "Scheduled",
  "startDateTime": "2026-06-24T09:00:00Z",
  "endDateTime": "2026-06-24T09:30:00Z",
  "patientUuid": "…",
  "patientName": "John Doe",
  "service": "General Medicine",
  "location": "Outpatient",
  "comments": "…"
}
```

**Verwacht:** HTTP `202`, body bevat `pendingNotifications`, `X-Api-Key` verplicht.

**Automatisering:** `OpenMrsWebhookApiTests`, `OpenMrsWebhookMapperTests`

## Demo-checklist (presentatie)

1. **Happy path:** webhook POST → logs Producer → scheduler → Consumer → Grafana metric `notification_dispatch_total`
2. **Cancel:** CANCELLED event → geen pending reminders in dashboard-SQL ([DASHBOARD_DATABASE.md](DASHBOARD_DATABASE.md))
3. **Resiliency:** toon RabbitMQ retry in logs (`EnsureConnectedWithRetry`) of Producer stop/start met outbox (OMOD)

## Scripts

```bash
dotnet test NotificationModule.sln --filter "FullyQualifiedName~OpenMrs"
docker compose --env-file env.example up -d
./scripts/smoke-test.sh
./scripts/comprehensive-test.sh
```
