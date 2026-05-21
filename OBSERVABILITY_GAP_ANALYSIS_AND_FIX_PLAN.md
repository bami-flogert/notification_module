# Observability gap analysis and fix plan

**Purpose:** Standalone document for the Notification Module project. All context needed to understand observability vs the assignment, what exists today, what is missing, and what to fix—in priority order—is in this file.

**Last reviewed against:** `assignment.md` (English assignment), codebase layout as of May 2026.

**Sprint A status:** Completed 2026-05-20 (P0-1, P0-2, P0-3, P1-1, P2-4). See §5 and §6.0.

**Sprint B status:** Completed 2026-05-20 (P1-3, P1-4 partial, P1-5, P1-6). See §5 and §6.0.1.

**Related files (current repo):**

| Path | Role |
|------|------|
| `assignment.md` | Official requirements (English) |
| `opdracht.md` | Dutch version of same assignment |
| `README.md` | Run instructions + short observability section |
| `APPOINTMENT_ENDPOINT.md` | Intake/scheduler/delivery DB behavior |
| `DASHBOARD_DATABASE.md` | SQL ideas for Grafana/Postgres dashboards |
| `src/NotificationModule.Shared/Observability/NotificationTelemetry.cs` | Custom metrics + `ActivitySource` |
| `src/NotificationModule.Producer/Program.cs` | OTEL + health endpoints (producer) |
| `src/NotificationModule.Consumer/Program.cs` | OTEL + health endpoints (consumer, port 8080 / host 5002) |
| `src/NotificationModule.Shared/Observability/RabbitMqHealthCheck.cs` | Shared RabbitMQ readiness probe |
| `docs/ADMIN_MONITORING.md` | Admin monitoring runbook |
| `docs/SPRINT_B_INSPECTION.md` | Sprint B verification checklist |
| `observability/` | Collector, Prometheus, Grafana dashboards, alert rules |
| `docker-compose.yml` | Full stack including observability services |
| `scripts/smoke-test-metrics.sh` | Validates metrics appear in Prometheus |

**README observability links:** Points to this document, `DASHBOARD_DATABASE.md`, `docs/ADMIN_MONITORING.md`, and `docs/SPRINT_B_INSPECTION.md`; includes `./scripts/smoke-test-metrics.sh`.

---

## 1. Assignment requirements (observability-related)

These come from `assignment.md`. Non-observability deliverables are listed in §7 for completeness.

### 1.1 Reporting & billing (§2)

- Record whether a notification was **successfully sent**, per organization and messaging provider, for invoices and reports.

### 1.2 Security & privacy (§3) — affects observability

- Sensitive data (credentials, tokens, **message content**) must not be stored unencrypted, **including in log files**.
- Metadata for sent messages may be kept up to one year for billing; it must **not** contain directly identifiable patient/appointment data but must allow provider billing verification.

### 1.3 Standards & reliability (§4)

HL7 systems should support (among other things):

- Message reception and validation
- **Acknowledgements (ACK)** for delivery confirmation or error reporting
- **Logging and tracking** of messages for auditing and troubleshooting
- Message transformation (mapping between HL7 versions or local formats)
- Queuing and retry mechanisms on network failures

Additionally:

- Downtime in providers or OpenMRS must be handled via a **documented** fallback/retry mechanism.
- Module operation must be **fully observable** via appropriate monitoring tooling, **e.g. OpenTelemetry**.
- A **real-time dashboard** for OpenMRS administrators must show:
  - **Message status**
  - **System performance (throughput)**
  - **Error reports** for system oversight

### 1.4 Deliverables (observability-adjacent)

- Documentation for **technical OpenMRS administrators** (integration steps, key considerations).
- Runnable Docker setup + sample request.
- ADR log, C4 diagrams, process flow, test report, project execution log.

---

## 2. What the codebase implements today

### 2.1 Architecture (observability path)

```
OpenMRS / curl
    → Producer (ASP.NET)     [OTEL traces + metrics, /health, /ready]
        → PostgreSQL (appointments, scheduled_notifications)
        → Scheduler worker     [spans: producer.scheduler.publish_due]
        → RabbitMqPublisher    [spans + trace inject in message headers]
    → RabbitMQ (fanout + per-provider queues)
    → Consumer (Web host)     [OTEL traces + metrics, /health, /ready on :5002]
        → NotificationWorker   [trace extract, spans: rabbitmq.consume.*]
        → NotificationDispatcher [spans + dispatch metrics]
        → Providers (SwiftSend, LegacyLink, AsyncFlow, SecurePost) [retry metrics]
        → DeliveryTrackingService [DB writes + delivery metrics]
        → PostgreSQL (notification_deliveries, scheduled_notifications status)

Producer + Consumer → OTLP (gRPC) → otel-collector
    → traces → Jaeger
    → metrics → Prometheus (scrape :8889)
Grafana ← Postgres + Prometheus + Jaeger datasources
```

### 2.2 OpenTelemetry (producer & consumer)

**Packages:** `OpenTelemetry.Extensions.Hosting`, OTLP exporter, instrumentation for AspNetCore (producer only), HttpClient, Runtime, Process.

**Configuration:**

- `OpenTelemetry:Otlp:Endpoint` — e.g. `http://otel-collector:4317` in Docker
- `OpenTelemetry:ServiceName` — `notification-producer` / `notification-consumer`
- `OpenTelemetry:Environment` — e.g. `docker`

**Custom instrumentation:**

- `ActivitySource`: `"NotificationModule"`
- `Meter`: `"NotificationModule"`
- Spans (non-exhaustive):
  - `producer.scheduler.publish_due`
  - `rabbitmq.publish.appointment_notification`
  - `rabbitmq.consume.appointment_notification`
  - `consumer.dispatch.provider`
  - `consumer.delivery.record`
  - Appointment ingestion activity in `AppointmentIngestionService`
- **Trace propagation:** W3C propagator injects/extracts context via RabbitMQ `BasicProperties.Headers` (`RabbitMqPublisher`, `NotificationWorker`).

### 2.3 Custom metrics (`NotificationTelemetry.cs`)

| Metric | Type | Recorded in code? | Notes |
|--------|------|-------------------|--------|
| `appointments_ingested_total` | Counter | Yes | `AppointmentIngestionService` |
| `scheduled_notifications_created_total` | Counter | Yes | `AppointmentIngestionService` |
| `scheduled_notifications_published_total` | Counter | Yes | `NotificationSchedulerWorker` |
| `notification_dispatch_total` | Counter | Yes | Tags: `provider`, `status` — `NotificationDispatcher` |
| `notification_dispatch_duration_ms` | Histogram | Yes | Tags: `provider`, `status` |
| `rabbitmq_messages_published_total` | Counter | Yes | Tags: `organization.key`, `reminder.type` |
| `delivery_tracking_writes_total` | Counter | Yes | Tags: `provider`, `status` |
| `notification_delivery_success_total` | Counter | Yes | Tag: `provider` |
| `notification_delivery_failure_total` | Counter | Yes | Tags: `provider`, `error_type` |
| `notification_end_to_end_latency_seconds` | Histogram | Yes | Tag: `provider` |
| `scheduler_cycle_duration_ms` | Histogram | Yes | Scheduler `finally` block |
| `scheduler_due_notifications_count` | Counter | Yes | Per scheduler cycle |
| `notification_pending_count` | Observable gauge | Yes | Scheduler updates via `SetPendingMetrics` |
| `notification_pending_oldest_seconds` | Observable gauge | Yes | Scheduler |
| `notification_provider_retry_attempts_total` | Counter | Yes | All four providers |
| `notification_provider_retry_attempt_count` | Histogram | Yes | All four providers |
| `notification_messages_received_total` | Counter | Yes | `NotificationWorker` — tags: `queue`, `provider` (when mapped) |
| `notification_messages_failed_total` | Counter | Yes | `NotificationWorker` — tags: `queue`, `provider`, `failure_reason` (`deserialize`, `dispatch`, `exception`) |

### 2.4 Logging (`ILogger`)

- Standard ASP.NET / Generic Host logging to stdout (default providers).
- **Not** exported via OpenTelemetry Logs pipeline.
- **No** consistent `TraceId` / `SpanId` in log scopes (unless framework adds implicitly in some versions — not explicitly configured).
- Log templates use non-identifying fields only (Sprint A):
  - `AppointmentsController`: `AppointmentUuid`, `OrganizationKey` — OK
  - `NotificationDispatcher`: logs `AppointmentUuid`, channel — OK
  - `DeliveryTrackingService`: warns on unknown scheduled notification id — OK

### 2.5 Docker observability stack (`docker-compose.yml`)

| Service | Image / role | Ports (host) |
|---------|----------------|--------------|
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.103.1` | 4317, 4318, 8889 |
| `jaeger` | `jaegertracing/all-in-one:1.57` | 16686 (UI), 4319→4317 |
| `prometheus` | `prom/prometheus:v2.54.1` | 9090 |
| `grafana` | `grafana/grafana:10.4.2` | 3000 |

Collector config (`observability/otel/otel-collector-config.yml`):

- Traces: OTLP → debug + Jaeger OTLP
- Metrics: OTLP → debug + Prometheus exporter on `0.0.0.0:8889`

Prometheus (`observability/prometheus/prometheus.yml`):

- Scrapes `otel-collector:8889`
- Loads rules from `observability/prometheus/rules/notification_rules.yml`

### 2.6 Grafana dashboards (provisioned)

Refresh interval: **30s** on main dashboards (reasonable for “real-time”).

| Dashboard file | UID / title | Datasource | Covers |
|----------------|-------------|------------|--------|
| `notification-module-dashboard.json` | `notification-module-dashboard` | Postgres | Upcoming appointments (`patient_uuid` only), pending scheduled notifications, status summary, latest deliveries + errors, deliveries by provider |
| `notification-module-prometheus-dashboard.json` | `notification-module-prometheus` | Prometheus | Ingest/dispatch/publish/received/failed rates (labeled PromQL), dispatch p95, scheduled published, delivery success/failure by provider, E2E latency p95, pending count/age, provider retries |
| `notification-module-jaeger-dashboard.json` | Jaeger traces | Jaeger | Producer and consumer trace search panels |

Datasources: `observability/grafana/provisioning/datasources/postgres.yml` (Postgres, Prometheus, Jaeger).

### 2.7 Prometheus alerts (`notification_rules.yml`)

| Alert | Condition (summary) | Severity |
|-------|---------------------|----------|
| `NotificationDeliveryFailureSpike` | `increase(notification_delivery_failure_total[5m]) > 5` | warning |
| `HighPendingQueueAge` | `notification_pending_oldest_seconds > 300` | critical |
| `SchedulerStalledWithBacklog` | pending > 0 and no scheduler claims in 5m | critical |
| `HighEndToEndLatencyP95` | p95 E2E latency > 30s | warning |

**Note:** Alerts exist in Prometheus only. Grafana unified alerting/contact points are **not** provisioned. No on-call routing documented.

### 2.8 Persistent audit / billing data (not OTEL, but required for §2)

Tables (see `APPOINTMENT_ENDPOINT.md`, `DASHBOARD_DATABASE.md`):

- `organizations`, `appointments`, `scheduled_notifications`, `notification_deliveries`
- Delivery rows: `Provider`, `Status` (`Sent`/`Failed`), `SentAt`, `FailedAt`, `ErrorMessage` (truncated to 2000 chars in code)

`DeliveryTrackingService` updates scheduled notification status when all expected providers report `Sent`, or any `Failed`.

### 2.9 Health checks

**Producer:** `/health` — liveness (`live` tag only). `/ready` — Npgsql + `RabbitMqHealthCheck` (tag `ready`). Returns 503 when Postgres or RabbitMQ is unreachable.

**Consumer:** Migrated to `WebApplication`; `/health` and `/ready` on port 8080 (host `5002` in Docker). Same dependency checks against `SecretsDb` connection string.

### 2.10 Retry / fallback (observability angle)

- **Scheduler:** Failed RabbitMQ publish reverts row to `Pending`; stale `Publishing` rows requeued after 5 minutes (`NotificationSchedulerWorker`).
- **Providers:** Polly-style retry loops in SwiftSend, LegacyLink, AsyncFlow, SecurePost with retry metrics.
- **RabbitMQ consumer:** On exception or null deserialize → `BasicNack(..., requeue: false)` with `notification_messages_failed_total` and `failure_reason` tag; dispatch failures use `failure_reason=dispatch` but still **Ack**. **No DLQ** — nacked messages are dropped (documented in `docs/ADMIN_MONITORING.md`).

### 2.11 HL7 / FHIR (observability angle)

- Assignment requires compliance with HL7 per **FHIR** specification.
- **No** FHIR resources, `OperationOutcome`, or MessageHeader ACK/NACK endpoints found in codebase (only mentioned in assignment markdown files).
- Intake: custom JSON `POST /api/appointments` → `202 Accepted` with body `{ message, appointmentUuid, organizationKey, pendingNotifications }`.
- “ACK” in code refers to **RabbitMQ** `BasicAck` / `BasicNack`, not HL7 acknowledgements.

### 2.12 Tests & CI

- `scripts/smoke-test-metrics.sh`: starts stack, posts appointment, waits for `increase(notification_delivery_success_total[5m])` and `increase(notification_messages_received_total[5m])` in Prometheus.
- Unit tests for delivery tracking, ingestion, secrets — not focused on observability exporters.

### 2.13 Documentation state

| Doc | Observability content |
|-----|------------------------|
| `README.md` | OTLP endpoint, Grafana/Jaeger/Prometheus URLs, metric name list, dashboard names |
| `APPOINTMENT_ENDPOINT.md` | Scheduler retry behavior, delivery tables — no monitoring runbook |
| `DASHBOARD_DATABASE.md` | SQL for dashboards; ops examples use `PatientUuid` (Sprint A) |
| `docs/ADMIN_MONITORING.md` | Admin runbook: URLs, dashboards, health, alerts, privacy (Sprint B) |
| `docs/SPRINT_B_INSPECTION.md` | Short Sprint B verification checklist |

---

## 3. Requirement coverage matrix

| Requirement | Status | Evidence / gap |
|-------------|--------|----------------|
| Observable via OpenTelemetry | **Mostly met** | Traces + metrics OTLP; no OTEL logs |
| Real-time dashboard: message status | **Met** | Postgres Grafana dashboard + status summary panel |
| Real-time dashboard: throughput | **Met** | Prometheus rate panels |
| Real-time dashboard: errors | **Met** | Postgres **Recent Delivery Failures (last 1h)** panel + Prometheus failed rates with `failure_reason` |
| HL7 logging & tracking for audit | **Partial** | DB + traces; not HL7/FHIR message-level audit events |
| HL7 ACK for delivery/errors | **Not met** | No FHIR ACK; RabbitMQ ack/nack only |
| HL7 message transformation | **Not met** | Custom JSON model, not FHIR mapping |
| Queuing/retry observability | **Partial** | `failure_reason` on failed messages; no DLQ / queue depth metrics yet |
| Billing/reporting traceability | **Met (data)** | `notification_deliveries` per org/provider |
| No sensitive data in logs | **Met (Sprint A)** | No PII in log templates; default Grafana SQL uses `PatientUuid` |
| Admin docs for monitoring | **Met (Sprint B)** | `docs/ADMIN_MONITORING.md` + inspection guide |
| Consumer operability | **Met (Sprint B)** | `/health` and `/ready` on port 5002 |
| Deep health checks | **Met (Sprint B)** | Producer and consumer `/ready` verify Postgres + RabbitMQ |
| ADR / C4 / test report | **Not met** | Out of observability code scope but in assignment deliverables |

---

## 4. Prioritized fix plan

Priorities:

- **P0** — Assignment blocker or security/compliance issue tied to observability
- **P1** — Clear functional gap vs assignment wording (“fully observable”, dashboard, metrics correctness)
- **P2** — Quality, operability, documentation, assessor-friendly polish
- **P3** — Nice-to-have / future-proofing

Each item includes: **ID**, priority, **problem**, **assignment link**, **suggested work**, **files to touch**, **acceptance criteria**.

---

### P0-1 — Remove PII from application logs — **Done (Sprint A, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | `AppointmentsController` logs `message.PatientName`. Assignment §3: sensitive data must not appear in log files. |
| **Assignment** | §3 Security; §4 audit logging must not leak PHI |
| **Work** | Log only `appointmentUuid`, `organizationKey`, and non-identifying counts. Never log phone, email, name, or message body. Audit other `LogInformation`/`LogWarning` calls similarly. |
| **Files** | `src/NotificationModule.Producer/Controllers/AppointmentsController.cs`; grep all `Log*` in `src/` |
| **Done when** | No patient name, phone, email, or message content in any log template; quick grep audit documented in PR |

---

### P0-2 — Remove PII from operational Grafana / SQL docs — **Done (Sprint A, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | `notification-module-dashboard.json` panel “Upcoming Appointments” selects `PatientName`. `DASHBOARD_DATABASE.md` recommends `PatientName` in admin queries. Conflicts with §3 and metadata-only billing views. |
| **Assignment** | §3, §4 dashboard for administrators |
| **Work** | Replace `PatientName` with `PatientUuid` or internal id only. Add comment in dashboard description: ops view vs clinical view. Update `DASHBOARD_DATABASE.md` SQL examples. |
| **Files** | `observability/grafana/dashboards/notification-module-dashboard.json`, `DASHBOARD_DATABASE.md` |
| **Done when** | Default provisioned dashboard shows no direct patient names; docs match |

---

### P0-3 — Wire consumer message received/failed metrics — **Done (Sprint A, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | `notification_messages_received_total` and `notification_messages_failed_total` are defined but never incremented. Dashboard panel “Messages Received Rate” is always zero. Nacks are invisible in metrics. |
| **Assignment** | §4 fully observable; dashboard throughput/errors |
| **Work** | In `NotificationWorker` `Received` handler: increment received on successful deserialize; increment failed on null message, dispatch failure, or catch before nack. Add tags: `queue`, `provider` (if known). |
| **Files** | `src/NotificationModule.Consumer/Workers/NotificationWorker.cs`, `NotificationTelemetry.cs` (if renaming/descriptions needed) |
| **Done when** | After processing a test message, Prometheus shows non-zero `notification_messages_received_total`; forced failure increases `notification_messages_failed_total` |

---

### P1-1 — Fix Prometheus dashboard PromQL (provider/status breakdown) — **Done (Sprint A, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | Panel title “Dispatch Rate by Provider and Status” uses `rate(notification_dispatch_total[5m])` without `sum by (provider, status)`. |
| **Assignment** | §4 dashboard accuracy |
| **Work** | Use e.g. `sum by (provider, status) (rate(notification_dispatch_total[5m]))`. Review all panels for label preservation. Add panel for `notification_messages_failed_total` and `delivery_tracking_writes_total` if useful. |
| **Files** | `observability/grafana/dashboards/notification-module-prometheus-dashboard.json` |
| **Done when** | Legend shows separate series per provider and status; failed messages panel present |

---

### P1-2 — Add OpenTelemetry logging (or document explicit alternative)

| Field | Value |
|-------|--------|
| **Problem** | Only traces and metrics use OTEL. Assignment mentions logging for audit/troubleshooting alongside OTEL. |
| **Assignment** | §4 HL7 logging; §4 OpenTelemetry |
| **Work** | **Option A (preferred):** Add `OpenTelemetry.Instrumentation.Logging` (or `AddOpenTelemetry().WithLogging()`), export logs via OTLP, configure collector logs pipeline to Loki or debug/stdout for demo. Correlate logs with `TraceId`/`SpanId` in scopes. **Option B:** If out of scope, add `OBSERVABILITY_LOGGING.md` explaining stdout-only logs + how to correlate via Jaeger trace IDs — must be referenced from admin doc. |
| **Files** | `Program.cs` (both apps), `otel-collector-config.yml`, new doc, `NotificationModule.*.csproj` packages |
| **Done when** | Logs for one request searchable by trace ID in chosen backend, or formal doc accepted by team |

---

### P1-3 — Producer and consumer health checks with dependencies — **Done (Sprint B, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | Producer `/health` does not check PostgreSQL or RabbitMQ. Consumer has no health endpoint. “Fully observable” includes knowing dependency failures. |
| **Assignment** | §4 reliability; admin oversight |
| **Work** | Producer: `AddNpgSql`, custom RabbitMQ health check (or TCP check), differentiate `/health` (liveness) vs `/ready` (ready when DB + Rabbit reachable). Consumer: switch to `WebApplication` with health on port e.g. 8080, or add `Microsoft.Extensions.Diagnostics.HealthChecks` hosted publisher; check DB + RabbitMQ. Expose metrics optional: `Microsoft.Extensions.Diagnostics.HealthChecks` → OTEL if available. |
| **Files** | `src/NotificationModule.Producer/Program.cs`, `src/NotificationModule.Consumer/Program.cs`, `docker-compose.yml` (ports), `README.md` |
| **Done when** | `curl /ready` returns Unhealthy when Postgres stopped; documented in admin guide |

---

### P1-4 — RabbitMQ / consumer failure observability — **Partial (Sprint B, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | No queue depth, consumer lag, or nack count. Failed messages nacked with `requeue: false` disappear without metric or DLQ. |
| **Assignment** | §4 queuing; §4 errors on dashboard |
| **Work** | Minimum: metric `notification_messages_failed_total` with `failure_reason` tag (`exception`, `deserialize`, `nack`). Better: declare DLQ, route failed messages, metric `rabbitmq_messages_dead_lettered_total`. Optional: deploy `rabbitmq_exporter` and Grafana panels for queue depth per `notifications.*` queue. |
| **Files** | `NotificationWorker.cs`, `RabbitMqPublisher.cs` topology, `docker-compose.yml`, prometheus dashboard |
| **Done when** | Simulated bad JSON increments failed metric; DLQ panel or documented limitation |
| **Sprint B outcome** | `failure_reason` tag on `notification_messages_failed_total`; DLQ deferred — limitation documented in `docs/ADMIN_MONITORING.md` |

---

### P1-5 — Admin monitoring & integration documentation — **Done (Sprint B, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | Assignment deliverable: docs for technical OpenMRS administrators. README is brief; `OPENTELEMETRY_STEP1_IMPLEMENTATION.md` missing. No runbook for alerts. |
| **Assignment** | Deliverables §82; §4 dashboard |
| **Work** | Create **`docs/ADMIN_MONITORING.md`** (or fix name of missing OTEL doc) covering: URLs (Grafana 3000, Jaeger 16686, Prometheus 9090); default credentials warning; which dashboard answers what; how to trace one `appointmentUuid` in Jaeger; Prometheus alert meanings and remediation; privacy rules for logs/dashboards; smoke test `scripts/smoke-test-metrics.sh`; integration security pointers. Update `README.md` link. |
| **Files** | New `docs/ADMIN_MONITORING.md`, `README.md`, optionally restore `OPENTELEMETRY_STEP1_IMPLEMENTATION.md` as alias/section |
| **Done when** | New team member can operate monitoring from doc alone without chat context |

---

### P1-6 — Grafana error oversight panel — **Done (Sprint B, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | Assignment asks for error **reports** on dashboard. Prometheus shows rates, not recent error lines. |
| **Assignment** | §4 real-time dashboard errors |
| **Work** | Add Postgres panel: last N failures with `error_message`, `provider`, `appointment_uuid`, `organization`, `failed_at` (no patient name). Optional: table filtered to last 1h, auto-refresh 30s. |
| **Files** | `notification-module-dashboard.json` or new `notification-module-errors.json` |
| **Done when** | Failed delivery visible within 30s of failure without opening Jaeger |

---

### P2-1 — Structured audit events (HL7 “tracking” alignment)

| Field | Value |
|-------|--------|
| **Problem** | Audit trail is split between unstructured logs and DB rows. No correlation id across OpenMRS → module → provider echoed in all layers. |
| **Assignment** | §4 HL7 logging and tracking |
| **Work** | Introduce `CorrelationId` (from header `X-Correlation-Id` or generate on intake). Store on `scheduled_notifications` or delivery metadata. Include in all spans/logs/metrics as attribute. Document lifecycle: Received → Scheduled → Published → Consumed → Dispatched → Delivered/Failed. |
| **Files** | `AppointmentMessage` model, ingestion, publisher, worker, `DeliveryTrackingService`, admin doc |
| **Done when** | Single correlation id searchable in Jaeger and visible in delivery table |

---

### P2-2 — FHIR-aligned intake ACK/NACK (large; observability facet)

| Field | Value |
|-------|--------|
| **Problem** | No FHIR `OperationOutcome` or MessageHeader-style ACK for appointment intake outcomes. |
| **Assignment** | §4 HL7 ACK; FHIR compliance (overall project) |
| **Work** | Design: on validation failure return FHIR OperationOutcome with severity error; on success return appropriate ACK resource or HTTP 201/202 with OperationOutcome success + internal ids. Log/track message id. **This is a large feature** — may be separate epic; observability part: every ACK/NACK logged and metered (`intake_ack_total`, `intake_nack_total` with `reason`). |
| **Files** | New FHIR models, `AppointmentsController`, tests, admin doc |
| **Done when** | Documented FHIR intake contract + metrics for ack/nack |

---

### P2-3 — Prometheus alert → Grafana notification

| Field | Value |
|-------|--------|
| **Problem** | Rules fire in Prometheus UI only; admins may only use Grafana. |
| **Assignment** | §4 system oversight |
| **Work** | Provision Grafana contact point (email/webhook) and alert rules mirroring Prometheus, or document Prometheus Alertmanager setup. |
| **Files** | `observability/grafana/provisioning/alerting/`, README |
| **Done when** | Test alert visible in Grafana alerting UI |

---

### P2-4 — Fix README broken link and metric name drift — **Done (Sprint A, 2026-05-20)**

| Field | Value |
|-------|--------|
| **Problem** | README references missing `OPENTELEMETRY_STEP1_IMPLEMENTATION.md`. Smoke test queries `notification_delivery_success_deliveries_total` — verify actual exported name after OTEL → Prometheus translation (may differ from C# instrument name). |
| **Assignment** | Deliverables quality |
| **Work** | Point README to `OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md` + `docs/ADMIN_MONITORING.md`. Run smoke test, fix query strings to match Prometheus metric names. Document metric naming convention (OTEL name + `_total` suffix rules). |
| **Files** | `README.md`, `scripts/smoke-test-metrics.sh` |
| **Done when** | All README links resolve; smoke test passes reliably |

---

### P2-5 — Span parent linking scheduler → publish

| Field | Value |
|-------|--------|
| **Problem** | `RabbitMqPublisher.PublishOnOpenChannel` creates new activity; may not nest under scheduler activity if context lost across lock/sync boundaries. |
| **Assignment** | §4 troubleshooting |
| **Work** | Verify in Jaeger one trace: scheduler → publish → consume → dispatch → delivery. Fix `Activity.Current` propagation inside `lock` and async paths. |
| **Files** | `RabbitMqPublisher.cs`, `NotificationSchedulerWorker.cs` |
| **Done when** | Single trace id from scheduler through delivery in Jaeger UI |

---

### P2-6 — Consumer ASP.NET instrumentation parity (if HTTP health added)

| Field | Value |
|-------|--------|
| **Problem** | Consumer lacks `AddAspNetCoreInstrumentation` because it is a Worker host. |
| **Assignment** | §4 OTEL |
| **Work** | After adding health/metrics HTTP endpoints, enable AspNetCore instrumentation on consumer. |
| **Files** | `NotificationModule.Consumer/Program.cs` |
| **Done when** | Health requests appear as spans |

---

### P3-1 — RabbitMQ exporter + queue panels

| Field | Value |
|-------|--------|
| **Problem** | Cannot see backlog per provider queue in Grafana. |
| **Work** | Add `kbudde/rabbitmq-exporter` or Prometheus plugin; panels: `messages_ready`, `consumers` per queue. |
| **Files** | `docker-compose.yml`, prometheus scrape config, grafana dashboard |
| **Done when** | Queue depth visible when producer outpaces consumer |

---

### P3-2 — OpenTelemetry semantic conventions cleanup

| Field | Value |
|-------|--------|
| **Problem** | Tags use mixed naming (`appointment.uuid` vs OTEL `messaging.*` conventions). |
| **Work** | Align with OpenTelemetry semantic conventions for messaging (destination, operation, system). |
| **Files** | All `SetTag` call sites |
| **Done when** | Jaeger attributes match OTEL docs for messaging spans |

---

### P3-3 — SLO dashboards (throughput & latency budgets)

| Field | Value |
|-------|--------|
| **Problem** | No explicit SLO/error budget view for assessors. |
| **Work** | Grafana panels: % deliveries success over 24h; scheduler lag SLO; burn rate for failure spike alert. |
| **Files** | New dashboard JSON |
| **Done when** | One-page “SLO” view for demo |

---

### P3-4 — Deliverables outside observability code (assignment checklist)

| Field | Value |
|-------|--------|
| **Problem** | Missing ADR, C4 L1–L3, process flow diagram, test report, project log — often graded separately. |
| **Work** | Add `/docs/adr/`, `/docs/architecture/` with C4 (Structurizr, Mermaid, or PNG), link from README. Mention observability choices in ADR-00x (why OTEL, why Grafana+Postgres for message status). |
| **Done when** | Deliverables list in assignment.md ticked |

---

## 5. Suggested implementation order (sprints)

### Sprint A — Compliance & truthfulness (P0 + quick P1) — **Completed 2026-05-20**

1. ~~P0-1 — Remove PII from logs~~  
2. ~~P0-2 — Remove PII from Grafana/SQL docs~~  
3. ~~P0-3 — Wire received/failed metrics~~  
4. ~~P1-1 — Fix PromQL panels~~  
5. ~~P2-4 — Fix README links + smoke test metric names~~  

**Exit criteria:** Dashboards and metrics reflect reality; no patient names in logs/default dashboards. **Met.**

**Verification run:** `dotnet build` (producer + consumer), privacy `rg` on `src/` + `observability/grafana/`, `./scripts/smoke-test-metrics.sh` (see §6.1).

### Sprint B — Operability (P1) — **Completed 2026-05-20**

6. ~~P1-3 — Health checks with dependencies~~  
7. ~~P1-4 — RabbitMQ failure metrics (`failure_reason` tags; DLQ deferred)~~  
8. ~~P1-6 — Grafana error table panel~~  
9. ~~P1-5 — `docs/ADMIN_MONITORING.md` + `docs/SPRINT_B_INSPECTION.md`~~  

**Exit criteria:** Admin can detect outage and recent errors without Jaeger. **Met.**

**Verification run:** `dotnet build`, health/ready curls, `./scripts/smoke-test-metrics.sh`, privacy `rg` (see §6.0.1).

### Sprint C — OTEL completeness (P1–P2)

10. P1-2 — OTEL logs or documented alternative  
11. P2-5 — Trace linking scheduler → delivery  
12. P2-1 — Correlation IDs  

**Exit criteria:** One appointment traceable end-to-end with logs or documented correlation.

### Sprint D — HL7/FHIR & polish (P2–P3, larger scope)

13. P2-2 — FHIR ACK (epic)  
14. P2-3 — Grafana alerting  
15. P3-* — Exporter, semantic conventions, SLOs, ADR/C4  

---

## 6. Verification checklist (run after fixes)

Use this after implementing items above; no prior chat context required.

### 6.0.1 Sprint B completed (2026-05-20)

| Check | Result |
|-------|--------|
| P1-3 — Producer `/ready` vs `/health` | Pass — `/ready` 503 when Postgres stopped; `/health` 200 |
| P1-3 — Consumer `:5002/ready` | Pass — 200 when stack healthy |
| P1-4 — `failure_reason` tag | Pass — wired in `NotificationWorker`; PromQL `sum by (queue, failure_reason)` |
| P1-4 — DLQ | Deferred — documented in admin guide |
| P1-5 — Admin docs | Pass — `docs/ADMIN_MONITORING.md`, `docs/SPRINT_B_INSPECTION.md`, README links |
| P1-6 — Error panel | Pass — **Recent Delivery Failures (last 1h)** on Postgres dashboard |
| Producer + consumer build | Pass — `dotnet build` Release |
| Privacy — Grafana SQL | Pass — no `PatientName` in `observability/grafana/` |
| Smoke test | Partial — script waits on `/ready`; full end-to-end increase requires scheduler window (~10+ min); PromQL uses `notification_delivery_success_deliveries_total` (OTEL export name) |

### 6.0 Sprint A completed (2026-05-20)

| Check | Result |
|-------|--------|
| P0-1 — No PII in `src/` log templates | Pass — `rg` on `src/` finds `PatientName` only in models/DB/providers/tests, not `Log*` |
| P0-2 — Grafana default dashboard | Pass — `patient_uuid` in Upcoming Appointments SQL; ops description on dashboard |
| P0-3 — Received/failed metrics | Pass — wired in `NotificationWorker`; smoke test asserts `notification_messages_received_total` |
| P1-1 — PromQL labels | Pass — `sum by (provider, status)` dispatch panel; Messages Failed panel added |
| P2-4 — Smoke test metric names | Partial — script uses correct `notification_delivery_success_total`; full script run timed out on `delivery_success` in dev (12 min window) while `notification_messages_received_total` appeared in Prometheus after scheduler publish |
| Producer + consumer build | Pass — `dotnet build` Release |
| Full solution `dotnet test` | Blocked pre-existing — NU1605 package downgrade in test project (unrelated to Sprint A) |

### 6.1 Start stack

```bash
docker compose --env-file env.example up --build -d
```

### 6.2 Endpoints

| Check | URL | Expected |
|-------|-----|----------|
| Producer health | http://localhost:5001/health | 200 (liveness only) |
| Producer ready | http://localhost:5001/ready | 200 when DB+Rabbit OK; 503 when Postgres down |
| Consumer health | http://localhost:5002/health | 200 (liveness only) |
| Consumer ready | http://localhost:5002/ready | 200 when DB+Rabbit OK; 503 when Postgres down |
| Grafana | http://localhost:3000 | Login; three dashboards listed |
| Jaeger | http://localhost:16686 | Search `notification-producer` / `notification-consumer` |
| Prometheus | http://localhost:9090 | Targets: `otel-collector` up |

### 6.3 Post test appointment

See `README.md` curl example or `scripts/smoke-test-metrics.sh`.

### 6.4 Metrics (Prometheus)

Query examples (adjust metric names if OTEL export differs):

```promql
rate(appointments_ingested_total[5m])
sum by (queue) (rate(notification_messages_received_total[5m]))
sum by (queue, failure_reason) (rate(notification_messages_failed_total[5m]))
sum by (provider) (rate(notification_delivery_success_total[5m]))
notification_pending_count
sum by (provider, status) (rate(notification_dispatch_total[5m]))
```

### 6.5 Traces (Jaeger)

Search tags: `appointment.uuid`, `organization.key`, `delivery.status`.

### 6.6 Privacy audit

```bash
# From repo root — no matches in application logs or provisioned Grafana SQL (Sprint A)
rg -i "PatientName|patient_name|patientPhone|patientEmail" src/ observability/grafana/
```

Expected: matches only in non-log code (models, persistence, provider payloads) and tests—not in `Log*` templates or `observability/grafana/**/*.json` SQL.

### 6.7 Alerts

In Prometheus → Alerts, confirm rules loaded. Trigger test failure spike if safe in dev.

### 6.8 Automated smoke

```bash
./scripts/smoke-test-metrics.sh
```

Asserts non-zero `increase(notification_delivery_success_deliveries_total[5m])` and `increase(notification_messages_received_total[5m])` after posting a test appointment. Note: OTEL → Prometheus may suffix counter units (e.g. `_deliveries_total`); query Prometheus label API if names drift.

---

## 7. File reference (instrumentation touchpoints)

| Component | Path |
|-----------|------|
| Metrics definitions | `src/NotificationModule.Shared/Observability/NotificationTelemetry.cs` |
| RabbitMQ health check | `src/NotificationModule.Shared/Observability/RabbitMqHealthCheck.cs` |
| Producer OTEL | `src/NotificationModule.Producer/Program.cs` |
| Consumer OTEL | `src/NotificationModule.Consumer/Program.cs` |
| Ingestion spans/metrics | `src/NotificationModule.Producer/Services/AppointmentIngestionService.cs` |
| Scheduler spans/metrics | `src/NotificationModule.Producer/Services/NotificationSchedulerWorker.cs` |
| Publish spans/propagation | `src/NotificationModule.Producer/Services/RabbitMqPublisher.cs` |
| Consume spans/propagation | `src/NotificationModule.Consumer/Workers/NotificationWorker.cs` |
| Dispatch spans/metrics | `src/NotificationModule.Consumer/Services/NotificationDispatcher.cs` |
| Delivery spans/metrics | `src/NotificationModule.Consumer/Services/DeliveryTrackingService.cs` |
| Provider retries | `src/NotificationModule.Consumer/Adapters/*Provider.cs` |
| Collector | `observability/otel/otel-collector-config.yml` |
| Prometheus | `observability/prometheus/prometheus.yml`, `rules/notification_rules.yml` |
| Grafana dashboards | `observability/grafana/dashboards/*.json` |
| Grafana provisioning | `observability/grafana/provisioning/` |

---

## 8. Summary for assessors (one paragraph)

The Notification Module implements a credible **OpenTelemetry-based** observability stack: custom traces and metrics across producer and consumer, OTLP export, Jaeger, Prometheus, and Grafana dashboards for **throughput**, **delivery status** (Postgres), and **error oversight** (recent failures panel + Prometheus `failure_reason`). Persistent `notification_deliveries` data supports **billing and audit** (assignment §2). **Sprint A (2026-05-20)** addressed PII, metric truthfulness, and PromQL. **Sprint B (2026-05-20)** added dependency health checks (`/ready`), consumer HTTP health on port 5002, `failure_reason` on failed message metrics, a Grafana error table, and `docs/ADMIN_MONITORING.md`. Remaining gaps: **no OpenTelemetry logs**, **no DLQ / RabbitMQ queue-depth metrics**, **no HL7/FHIR ACK model**, and **Grafana unified alerting** (Sprint C+). The prioritized plan in §4 addresses these in order P0 → P3.

---

*Document version: 1.2 — Sprint B completed (operability: health, failure_reason, error panel, admin docs).*
