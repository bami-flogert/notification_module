# Sprint C inspection guide

Short checklist to review **Sprint C — Observability sign-off** (P1-2 log correlation docs, P2-5 Jaeger E2E verification). Full operations guide: [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md).

## What changed

| Area | Files |
|------|--------|
| Log correlation | `docs/ADMIN_MONITORING.md` — § Logging and trace correlation |
| Jaeger runbook | `docs/ADMIN_MONITORING.md` — § Trace one appointment (demo runbook) |
| Sign-off | This file, `OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md` §6.0.2 |

No runtime code changes (trace propagation already correct).

## 5-minute verification

### 1. Documentation

Confirm [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md) contains:

- **Logging and trace correlation** — three channels (traces, metrics, stdout); no OTEL log export; correlate via `appointment.uuid`
- **Trace one appointment** — 70-minute `startDateTime` demo, expected span names, two-trace note (ingest vs pipeline)

### 2. Start stack

```bash
docker compose --env-file env.example up --build -d
curl -sf http://localhost:5001/ready
curl -sf http://localhost:5002/ready
```

### 3. Post test appointment and search Jaeger

Follow the runbook in [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md) § Trace one appointment, or run:

```bash
./scripts/smoke-test-metrics.sh
```

Use the posted `appointmentUuid` in Jaeger → Tags: `appointment.uuid=<uuid>`.

### 4. Expected spans (verified 2026-05-21)

| When | Service | Span |
|------|---------|------|
| Immediately after POST | `notification-producer` | `producer.appointment.ingest` |
| ~10–15 min later (1h reminder) | `notification-producer` | `producer.scheduler.publish_due`, `rabbitmq.publish.appointment_notification` |
| Same pipeline trace | `notification-consumer` | `rabbitmq.consume.appointment_notification`, `consumer.dispatch.provider`, `consumer.delivery.record` |

**Verification run (2026-05-21):** Appointment `sprint-c-trace-20260521152954` — ingest trace and full pipeline trace observed via Jaeger API; Prometheus `notification_messages_received_total` increased. **Pass.**

## Admin quick reference

| Need | Where |
|------|--------|
| How logs relate to OpenTelemetry | [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md) § Logging and trace correlation |
| Trace one appointment (assessor demo) | [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md) § Trace one appointment |
| Grafana status / errors | Sprint B panels — [`SPRINT_B_INSPECTION.md`](SPRINT_B_INSPECTION.md) |

## Out of scope (Sprint C)

OTEL log export (Loki), DLQ, RabbitMQ exporter, Grafana contact points, FHIR ACK — see gap analysis doc §4 deferred items. Sprint D covers ADR, C4, test report, project log.
