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

Throughput, latency, and failure **rates**:

- Ingest, dispatch, publish, received, and failed message rates.
- Dispatch duration p95, end-to-end latency p95, pending queue age.
- **Messages Failed Rate** — broken down by `queue` and `failure_reason` (`deserialize`, `dispatch`, `exception`).

Use this dashboard for **system performance** and trend analysis.

### Notification Module - Jaeger Traces

End-to-end request flow:

- Producer: appointment ingestion, scheduler publish.
- Consumer: RabbitMQ consume, provider dispatch, delivery recording.

Search by tags: `appointment.uuid`, `organization.key`, `delivery.status`.

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

## Trace one appointment

1. Open Jaeger → **Search**.
2. Service: `notification-producer` or `notification-consumer`.
3. Tags: `appointment.uuid=<uuid>` or `organization.key=<key>`.
4. Open a trace to see scheduler → publish → consume → dispatch → delivery spans.

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

Expects non-zero `notification_delivery_success_deliveries_total` and `notification_messages_received_total` in Prometheus after posting a test appointment (OTEL export names may add unit suffixes).

## Known limitations

| Topic | Status |
|-------|--------|
| OpenTelemetry logs | Not exported yet — stdout logs only; correlate via Jaeger trace IDs |
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
