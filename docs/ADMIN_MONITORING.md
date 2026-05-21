# Admin monitoring guide

Guide for technical OpenMRS administrators operating the Notification Module observability stack.

## Service URLs (Docker default)

| Service | URL | Notes |
|---------|-----|-------|
| Grafana | http://localhost:3000 | Default login `admin` / `admin` — change in production |
| Jaeger | http://localhost:16686 | Distributed traces |
| Prometheus | http://localhost:9090 | Metrics and alert rules |
| RabbitMQ management | http://localhost:15672 | Default `guest` / `guest` |
| Producer API | http://localhost:5001 | Appointment intake |
| Producer health | http://localhost:5001/health | Liveness (process up) |
| Producer ready | http://localhost:5001/ready | Readiness (Postgres + RabbitMQ) |
| Consumer health | http://localhost:5002/health | Liveness |
| Consumer ready | http://localhost:5002/ready | Readiness (Postgres + RabbitMQ) |

Start the stack:

```bash
docker compose --env-file env.example up --build -d
```

## Which dashboard answers what

### Notification Module (PostgreSQL)

Operational message status from the database:

- **Upcoming Appointments** — scheduled appointments (`patient_uuid` only, no names).
- **Pending Scheduled Notifications** — backlog waiting for the scheduler.
- **Notification Status Summary** — counts by status for scheduled notifications and deliveries.
- **Latest Provider Deliveries** — recent delivery rows (all statuses).
- **Deliveries by Provider** — sent vs failed totals per provider.
- **Recent Delivery Failures (last 1h)** — failed deliveries with `error_message`, `appointment_uuid`, and organization (refreshes every 30s).

Use this dashboard when you need **current message status** and **recent error lines** without opening Jaeger.

### Notification Module - Prometheus Metrics

Throughput, latency, and failure **rates**. Panel PromQL uses **OTEL-exported** metric names (they may differ from C# instrument names in code). Examples you can paste into Prometheus → Graph:

```promql
rate(appointments_ingested_total[5m])
sum by (provider, status) (rate(notification_dispatch_dispatches_total[5m]))
sum by (provider) (rate(notification_delivery_success_deliveries_total[5m]))
notification_pending_count_notifications
```

Panels include ingest, dispatch, publish, received, delivery success/failure, scheduler metrics, pending queue age, and **Messages Failed Rate** (`queue`, `failure_reason`).

Use this dashboard for **system performance** and trend analysis.

### Notification Module - Jaeger Traces

**Grafana:** two tables list the 20 most recent traces per service (`notification-producer`, `notification-consumer`). Click a trace ID to open detail in Grafana when drill-down is configured.

**Jaeger UI** (http://localhost:16686) remains the primary tool for tag search and span detail:

- Producer: appointment ingestion, scheduler publish.
- Consumer: RabbitMQ consume, provider dispatch, delivery recording.

Search by tags: `appointment.uuid`, `organization.key`, `delivery.status`.

## Logging and trace correlation

Assignment §4 “fully observable” is covered by **three channels**. They are complementary; you do not need a separate log stack (Loki, OTEL Logs) for the assignment minimum.

| Channel | Where | Use for |
|---------|--------|---------|
| **Traces** | Jaeger (OpenTelemetry OTLP) | End-to-end timing, span hierarchy, `appointment.uuid` tags |
| **Metrics** | Prometheus → Grafana dashboards | Throughput, failure rates, queue age, alerts |
| **Logs** | Container stdout (`docker compose logs producer consumer`) | Human-readable events; **not** exported to OTLP |

### Why logs stay on stdout

- `ILogger` output goes to stdout only. There is **no** OpenTelemetry Logs pipeline and **no** `TraceId` / `SpanId` in log scopes.
- HL7-style **logging and tracking** for this module is satisfied by: persistent rows in `notification_deliveries`, OpenTelemetry traces, and PII-safe stdout logs.
- Loki or OTEL log export is optional polish (deferred); see the gap analysis doc.

### Correlate logs with Jaeger (no trace ID in logs)

1. Find a log line that mentions the appointment, for example:
   - Producer: `Saved appointment {AppointmentUuid} for organization {OrganizationKey}`
   - Consumer: `Sending via {Channel} for {Uuid}` or `{Channel} failed for {Uuid}`
2. Copy the **appointment UUID** from that line (not patient name or contact data).
3. Open Jaeger → **Search** → set **Tags** to `appointment.uuid=<uuid>` (optionally add `organization.key=<key>`).
4. Open the trace(s) to see spans for that appointment.

Optional — filter container logs by UUID:

```bash
# Linux / macOS
docker compose logs producer consumer 2>&1 | grep -i "<appointment-uuid>"

# Windows PowerShell
docker compose logs producer consumer 2>&1 | Select-String -Pattern "<appointment-uuid>" -CaseSensitive:$false
```

Log templates intentionally avoid patient names, phone, email, and message bodies (Sprint A privacy rules).

## Health endpoints

| Endpoint | Meaning | HTTP when dependencies down |
|----------|---------|------------------------------|
| `/health` | Liveness — application process is running | 200 |
| `/ready` | Readiness — PostgreSQL and RabbitMQ are reachable | 503 |

Example checks:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5001/health
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5001/ready
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5002/ready
```

If `/ready` returns **503**, check Postgres and RabbitMQ containers (`docker compose ps`, `docker compose logs postgres rabbitmq`).

## Trace one appointment (demo runbook)

Use this to verify end-to-end tracing for an assessor demo. The **1h** reminder is used so the scheduler publishes within about **10–15 minutes** (scheduler polls every 30s).

### 1. Start and check readiness

```bash
docker compose --env-file env.example up --build -d
curl -s -o /dev/null -w "producer ready: %{http_code}\n" http://localhost:5001/ready
curl -s -o /dev/null -w "consumer ready: %{http_code}\n" http://localhost:5002/ready
```

Both should return **200**.

### 2. Post a test appointment

Set `startDateTime` to **~70 minutes in the future** (UTC) so the **1h** reminder becomes due in ~10 minutes. Use a unique `appointmentUuid`.

```bash
# Example: adjust START for your shell (PowerShell example)
$start = (Get-Date).ToUniversalTime().AddMinutes(70).ToString("yyyy-MM-ddTHH:mm:ssZ")
$uuid = "trace-demo-$(Get-Date -Format 'yyyyMMddHHmmss')"
curl -sS -X POST "http://localhost:5001/api/appointments/default" `
  -H "Content-Type: application/json" `
  -d "{ \"appointmentUuid\": \"$uuid\", \"organizationKey\": \"default\", \"patientUuid\": \"patient-trace-demo\", \"patientName\": \"Trace Demo\", \"patientPhone\": \"+31610000099\", \"patientEmail\": \"trace@example.com\", \"startDateTime\": \"$start\", \"status\": \"Confirmed\", \"location\": \"Demo\", \"instructions\": \"Jaeger trace demo\" }"
echo "appointmentUuid=$uuid"
```

On Linux/macOS, use the same JSON pattern as [`scripts/smoke-test-metrics.sh`](../scripts/smoke-test-metrics.sh) (`date -u -d '+70 minutes'` or `date -u -v+70M`).

### 3. Jaeger — immediately after POST

Open http://localhost:16686 → **Search**:

| Field | Value |
|-------|--------|
| Service | `notification-producer` |
| Tags | `appointment.uuid=<your-uuid>` |

Expect a trace containing span **`producer.appointment.ingest`**. This is the **intake trace** at POST time.

### 4. Jaeger — after ~10–15 minutes

Search again with **Service = All** and tag `appointment.uuid=<your-uuid>`.

Expect a **pipeline trace** (linked via RabbitMQ W3C propagation) with spans such as:

| Service | Span name |
|---------|-----------|
| `notification-producer` | `producer.scheduler.publish_due` |
| `notification-producer` | `rabbitmq.publish.appointment_notification` |
| `notification-consumer` | `rabbitmq.consume.appointment_notification` |
| `notification-consumer` | `consumer.dispatch.provider` |
| `notification-consumer` | `consumer.delivery.record` |

**Note:** Intake (step 3) and pipeline (step 4) are **two separate traces** with the same `appointment.uuid` tag. The pipeline trace proves publish → consume propagation.

### 5. Cross-check delivery

- Grafana → **Notification Module** → delivery panels or **Recent Delivery Failures** (if failed).
- Prometheus: `increase(notification_delivery_success_deliveries_total[15m])` or run `./scripts/smoke-test-metrics.sh`.

### Quick search (any time)

1. Jaeger → **Search** → Service: **All** (or `notification-producer` / `notification-consumer`).
2. Tags: `appointment.uuid=<uuid>` or `organization.key=<key>`.
3. Open a trace to inspect span timeline and tags (`delivery.status`, `provider`, etc.).

## Prometheus alerts

Rules are in `observability/prometheus/rules/notification_rules.yml`. View firing alerts at http://localhost:9090/alerts.

| Alert | Severity | Meaning | First steps |
|-------|----------|---------|-------------|
| `NotificationDeliveryFailureSpike` | warning | More than 5 delivery failures in 5 minutes | Check **Recent Delivery Failures** in Grafana; verify provider (Comworld) availability |
| `HighPendingQueueAge` | critical | Oldest pending notification older than 5 minutes | Check scheduler logs; verify producer `/ready` and RabbitMQ |
| `SchedulerStalledWithBacklog` | critical | Pending backlog but scheduler not publishing | Restart producer; check DB connectivity |
| `HighEndToEndLatencyP95` | warning | p95 latency above 30 seconds | Check consumer load and provider response times |

Grafana unified alerting is not provisioned; use Prometheus UI or configure Alertmanager separately.

## Privacy and dashboards

- Application logs must not contain patient names, phone numbers, email, or message bodies.
- Default Grafana SQL uses **`PatientUuid`** and **`AppointmentUuid`** only — not `PatientName` or contact fields.
- Restrict Grafana access per organization in production.

See also [`DASHBOARD_DATABASE.md`](../DASHBOARD_DATABASE.md) for SQL examples.

## Smoke test

Validates metrics end-to-end (starts Docker if needed):

```bash
./scripts/smoke-test-metrics.sh
```

Asserts ingest soon after POST, then non-zero increases for `notification_dispatch_dispatches_total`, `notification_delivery_success_deliveries_total`, and `notification_messages_received_total` after the scheduler window (up to ~12 minutes).

## Known limitations

| Topic | Status |
|-------|--------|
| OpenTelemetry logs | Not exported — see [Logging and trace correlation](#logging-and-trace-correlation) |
| Dead-letter queue (DLQ) | Not implemented — messages nacked without requeue are **dropped** |
| RabbitMQ queue depth | Not in Grafana — use RabbitMQ management UI |
| FHIR ACK/NACK | Custom JSON intake only — see assignment / gap analysis doc |

## Integration security

- Provider credentials are encrypted in PostgreSQL (AES-256-GCM).
- Master key and seed values come from environment variables only — see [`README.md`](../README.md) and `env.example`.
- Never commit `.env` with production secrets.

## Further reading

- [`OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md`](../OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md) — full gap analysis and sprint backlog
- [`docs/SPRINT_B_INSPECTION.md`](SPRINT_B_INSPECTION.md) — short verification checklist for Sprint B changes
- [`docs/SPRINT_C_INSPECTION.md`](SPRINT_C_INSPECTION.md) — Sprint C log correlation and Jaeger E2E verification
