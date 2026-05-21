# Observability dashboard considerations

**Purpose:** Research notes on the three provisioned Grafana dashboards (`observability/grafana/dashboards/`), what is broken or incomplete, how an assessor can validate them, and whether to fix Grafana, use Jaeger/Prometheus directly, or document queries elsewhere.

**Evidence date:** 2026-05-21 — live checks against a running Docker stack (`docker compose --env-file env.example up -d`) with Prometheus at `http://127.0.0.1:9090` and Jaeger at `http://127.0.0.1:16686`.

---

## 1. Summary


| Dashboard                                    | Datasource | Overall   | Main issue                                                                                                                                                                                                     |
| -------------------------------------------- | ---------- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Notification Module**                      | PostgreSQL | Works     | Assignment “real-time dashboard” for **message status** and **errors** is met here.                                                                                                                            |
| **Notification Module - Prometheus Metrics** | Prometheus | Partial   | Several panels use **C# instrument names**; Prometheus exposes **OTEL-translated names** with unit suffixes. Some panels show data; others are empty or “No data”.                                             |
| **Notification Module - Jaeger Traces**      | Jaeger     | Broken UX | **Traces** panel type + legacy `query: "service=…"` target does not bind **Service Name** in the Jaeger query editor → “no service selected”, no traces. Jaeger UI at port 16686 works with the same services. |


**Already addressed in gap analysis (runtime fixes done; dashboard JSON may still lag):**

- P0-2 — Postgres dashboard PII (`PatientUuid` only) — **done**
- P0-3 — `notification_messages_received_total` wired — **done** (panel can work if metric name matches export)
- P1-1 — PromQL `sum by (provider, status)` for dispatch — **done in JSON**, but metric name in Prometheus is `notification_dispatch_dispatches_total`, not `notification_dispatch_total`
- P1-6 — Postgres **Recent Delivery Failures** panel — **done**
- P2-5 — E2E trace verification in Jaeger — **documented** in `docs/ADMIN_MONITORING.md`; **Grafana Jaeger dashboard not fixed**

**Not yet addressed for dashboards:**

- OTEL → Prometheus **metric name alignment** across dashboard JSON, alert rules, and `scripts/smoke-test-metrics.sh` (partially documented in gap plan P2-4 / §6.8, not fixed in dashboard)
- Grafana **Jaeger** dashboard panel model (P2-5 covers Jaeger UI, not Grafana embedding)
- Optional metrics never given panels (`delivery_tracking_writes_total`, `scheduler_cycle_duration_ms`, etc.)

---

## 2. Architecture reminder

```
Producer + Consumer → OTLP → otel-collector
  → traces → Jaeger (:16686)
  → metrics → Prometheus scrape :8889
Grafana (:3000) ← Postgres + Prometheus + Jaeger datasources
```

Service names in Jaeger (verified via `/api/services`): `notification-producer`, `notification-consumer` (plus `jaeger-all-in-one`).

---

## 3. PostgreSQL dashboard (works)

**File:** `observability/grafana/dashboards/notification-module-dashboard.json`

**Why it works:** Panels use raw SQL against application tables; datasource variable `${datasource}` resolves to provisioned Postgres (`observability/grafana/provisioning/datasources/postgres.yml`). No OTEL naming layer.

**Covers assignment §4 well:**

- Message status (appointments, pending scheduled notifications, status summary, deliveries)
- Error reports (**Recent Delivery Failures (last 1h)**) — gap **P1-6 done**

**Gap analysis:** **P0-2**, **P1-6** — addressed.

**Caveats for assessors:**

- “Upcoming” and backlog depend on data in DB; empty panels after fresh deploy are normal until appointments are posted.
- Failure panel is empty until a provider returns `Failed` (expected).

---

## 4. Prometheus dashboard (partial — root cause)

**File:** `observability/grafana/dashboards/notification-module-prometheus-dashboard.json`

### 4.1 OTEL → Prometheus naming

The collector’s Prometheus exporter rewrites many instrument names using the **unit** declared in `NotificationTelemetry.cs`. Counters named `*_total` often become `*_{unit}_total`. Histograms with unit `ms` gain a `_milliseconds_` segment in bucket/count/sum names.

**Names observed in Prometheus (2026-05-21):**


| C# / dashboard PromQL                            | Actual Prometheus name                                  | Dashboard panel                           |
| ------------------------------------------------ | ------------------------------------------------------- | ----------------------------------------- |
| `appointments_ingested_total`                    | `appointments_ingested_total`                           | Appointments Ingested Rate — **OK**       |
| `notification_messages_received_total`           | `notification_messages_received_total`                  | Messages Received Rate — **OK**           |
| `rabbitmq_messages_published_total`              | `rabbitmq_messages_published_total`                     | RabbitMQ Published Rate — **OK**          |
| `scheduled_notifications_published_total`        | `scheduled_notifications_published_total`               | Scheduled Published — **OK**              |
| `notification_dispatch_total`                    | `notification_dispatch_dispatches_total`                | Dispatch by provider/status — **broken**  |
| `notification_dispatch_duration_ms_bucket`       | `notification_dispatch_duration_ms_milliseconds_bucket` | Dispatch p95 — **broken**                 |
| `notification_delivery_success_total`            | `notification_delivery_success_deliveries_total`        | Delivery success — **broken**             |
| `notification_delivery_failure_total`            | *(not present until first failure sample)*              | Delivery failure — **empty / broken**     |
| `notification_end_to_end_latency_seconds_bucket` | same                                                    | E2E p95 — **OK**                          |
| `notification_pending_count`                     | `notification_pending_count_notifications`              | Pending count — **broken**                |
| `notification_pending_oldest_seconds`            | same                                                    | Oldest pending — **OK**                   |
| `notification_provider_retry_attempts_total`     | same                                                    | Provider retries — **OK**                 |
| `notification_messages_failed_total`             | *(not present until first failure)*                     | Messages failed — **empty until failure** |


**Gap analysis:** **P1-1** fixed PromQL aggregation (`sum by (...)`) but **not** OTEL export names. **P2-4** / §6.8 note smoke test uses `notification_delivery_success_deliveries_total` while dashboard still uses `notification_delivery_success_total`.

**Alert rules** (`observability/prometheus/rules/notification_rules.yml`) also use pre-export names for some expressions, e.g. `notification_delivery_failure_total`, `scheduler_due_notifications_count` vs exported `scheduler_due_notifications_count_total`.

### 4.2 Metrics implemented but not on dashboard

From `NotificationTelemetry.cs` and gap plan §2.3 — useful for assessors or a query file:

- `scheduled_notifications_created_total`
- `delivery_tracking_writes_total`
- `scheduler_cycle_duration_ms` → `scheduler_cycle_duration_ms_milliseconds_*`
- `scheduler_due_notifications_count` → `scheduler_due_notifications_count_total`
- `notification_provider_retry_attempt_count` (histogram)

**Gap analysis:** P1-1 suggested optional panels for `delivery_tracking_writes_total` — **not added**.

### 4.3 Timing / “no data” vs “wrong query”

Even with correct PromQL, several series only move after:

1. `POST /api/appointments` (ingest counters),
2. Scheduler window (~10–15 minutes for demo appointment with 1h reminder),
3. Consumer dispatch (delivery, E2E latency, received).

Assessors should distinguish **empty because idle** vs **empty because query returns nothing** (Prometheus → Graph → run the panel expr; “No data” with no series in autocomplete = wrong name).

### 4.4 Fix options (Prometheus / Grafana)


| Option                                                                                                        | Effort | Pros                                                             | Cons                                                                                             |
| ------------------------------------------------------------------------------------------------------------- | ------ | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| **A. Update dashboard + alert PromQL** to exported names                                                      | Low    | Keeps single Grafana entry point; matches smoke test             | Must re-verify after OTEL/collector upgrades                                                     |
| **B. Rename C# instruments** to match dashboard (drop or align units)                                         | Medium | Stable names in code and Grafana                                 | Touches telemetry + docs; may still change with exporter version                                 |
| **C. Add `prometheus/prometheus-queries.md`** (or similar) with canonical PromQL; keep Grafana as best-effort | Low    | Assessor can validate in Prometheus UI without fixing all panels | Assignment asks for dashboard in Grafana — Postgres covers status/errors; Prometheus part weaker |
| **D. Drop Prometheus Grafana dashboard**; use Prometheus UI + doc only                                        | Low    | Truthful — no false “working” panels                             | Weaker “one pane of glass” story                                                                 |
| **E. Recording rules / metric relabel** in Prometheus to alias old names                                      | Medium | Dashboard JSON unchanged                                         | Extra ops complexity, duplicate series risk                                                      |


**Recommendation for assignment minimum:** **A + C** — fix panel expressions and alerts to match live export names; add a short query reference file for assessors (see §7).

---

## 5. Jaeger dashboard (broken in Grafana; works in Jaeger UI)

**File:** `observability/grafana/dashboards/notification-module-jaeger-dashboard.json`

### 5.1 What the JSON does today

Two **Traces** panels with targets:

```json
{
  "query": "service=notification-producer",
  "queryType": "search",
  "refId": "A"
}
```

(and the same for `notification-consumer`).

### 5.2 Why Grafana shows “no service selected”

1. **Query model mismatch:** Current Jaeger datasource expects **Search** queries to set **Service Name** as a dedicated field (`service`), not only a logfmt string in `query`. Provisioned JSON does not set `service`, so the UI shows no service and the panel does not run a valid search.
2. **Panel type mismatch:** The **Traces** visualization is intended for a **single trace** (typically **TraceID** query type). **Search** returns a *list* of traces; Grafana maintainers recommend a **Table** (or Explore) for search results, then open one trace in **Traces** / trace view. Using Search inside Traces panels is a known source of empty or error states (including undefined `serviceName` on spans).
3. **Services exist:** Jaeger API lists `notification-producer` and `notification-consumer` — the problem is Grafana dashboard configuration, not missing telemetry.

**Gap analysis:** **P2-5** — verify E2E trace in **Jaeger UI** and document in `docs/ADMIN_MONITORING.md` — **addressed for Jaeger UI**, not for this Grafana dashboard file.

### 5.3 Fix options (Jaeger / Grafana)


| Option                                                                                                                                                       | Effort                                          | Pros                                       | Cons                                                  |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------- | ------------------------------------------ | ----------------------------------------------------- |
| **A. Fix provisioned dashboard** — `queryType: search`, `service: notification-producer`, Table panel listing traces; optional second panel TraceID via link | Medium                                          | Single Grafana folder for assessors        | More JSON maintenance; two-step UX                    |
| **B. Add dashboard variables** — `service` query `notification-producer                                                                                      | notification-consumer`, tags` appointment.uuid` | Medium                                     | Interactive demo                                      |
| **C. Remove Jaeger Grafana dashboard**; link from Postgres/Prometheus dashboards to `http://localhost:16686`                                                 | Low                                             | Honest — tracing works where it works best | One fewer provisioned dashboard                       |
| **D. Grafana Explore only** — document saved Explore queries in `docs/ADMIN_MONITORING.md`                                                                   | Low                                             | No broken panels                           | Not a “dashboard” in folder list                      |
| **E. Embed Jaeger UI** (iframe panel)                                                                                                                        | Low–medium                                      | Looks integrated                           | Security/embed restrictions; not ideal for production |


**Recommendation:** For assessor demos, **C or D** is acceptable if **ADMIN_MONITORING** trace runbook is followed; for a polished Grafana story, **A** (Table + TraceID) or **B**.

---

## 6. How an assessor can validate (step-by-step)

No prior chat context required. Aligns with gap plan §6 and `docs/ADMIN_MONITORING.md` / `docs/SPRINT_B_INSPECTION.md`.

### 6.1 Start stack and health

```bash
docker compose --env-file env.example up --build -d
curl -s -o /dev/null -w "producer ready: %{http_code}\n" http://localhost:5001/ready
curl -s -o /dev/null -w "consumer ready: %{http_code}\n" http://localhost:5002/ready
```

Expect **200** on `/ready`.

### 6.2 PostgreSQL dashboard (expect pass)

1. Open Grafana → [http://localhost:3000](http://localhost:3000) (default `admin` / `admin`).
2. Folder **Notification Module** → **Notification Module**.
3. Post a test appointment (`README.md` or `scripts/smoke-test-metrics.sh` JSON pattern).
4. Within ~30s refresh: **Upcoming Appointments**, **Pending Scheduled Notifications**, **Notification Status Summary** show rows.
5. After scheduler + delivery (~10–15 min for 1h reminder demo): **Latest Provider Deliveries**, **Deliveries by Provider** update.
6. Confirm **no** `PatientName` in SQL panels (`rg -i PatientName observability/grafana/` → no matches).

**Pass:** Status and throughput-of-record (DB rows) visible; error panel structure exists (rows only if failures occurred).

### 6.3 Prometheus dashboard (expect partial)

1. Open **Notification Module - Prometheus Metrics**.
2. In parallel, Prometheus → [http://localhost:9090](http://localhost:9090) → **Graph** → **Metrics explorer** (or `api/v1/label/__name__/values`).
3. For each panel title, run the panel’s expression; if empty, try the **exported name** from §4.1 table.
4. After smoke test or scheduler window, confirm at least:
  - `appointments_ingested_total` rate > 0
  - `notification_messages_received_total` rate > 0
  - `notification_delivery_success_deliveries_total` rate > 0 (not `notification_delivery_success_total`)

**Pass (assignment-aligned):** Assessor can show **throughput** via working panels *or* Prometheus UI with correct PromQL. **Fail** if assessor only trusts Grafana Prometheus dashboard without checking Prometheus — several panels currently misleading.

**Optional script:**

```bash
./scripts/smoke-test-metrics.sh
```

Uses correct delivery metric name; still uses `notification_messages_received_total` (matches export).

### 6.4 Jaeger (use UI for reliable pass)

1. **Do not rely** on Grafana **Notification Module - Jaeger Traces** until fixed (§5).
2. Open [http://localhost:16686](http://localhost:16686).
3. Immediately after `POST /api/appointments`: Service `notification-producer`, Tags `appointment.uuid=<uuid>` → expect `producer.appointment.ingest` span.
4. After ~10–15 minutes: Service **All**, same tag → pipeline spans (`producer.scheduler.publish_due`, `rabbitmq.publish.`*, `rabbitmq.consume.`*, `consumer.dispatch.provider`, `consumer.delivery.record`).

**Pass:** Trace search by `appointment.uuid` in **Jaeger UI**. Documented in `docs/ADMIN_MONITORING.md` (**P2-5** runbook).

### 6.5 Assignment §4 mapping for assessors


| Requirement                | Best proof today                                                                             |
| -------------------------- | -------------------------------------------------------------------------------------------- |
| Message status             | Postgres Grafana dashboard                                                                   |
| Throughput                 | Postgres counts + Prometheus (UI or fixed panels)                                            |
| Error reports              | Postgres **Recent Delivery Failures** + Prometheus failed rates (when failures exist)        |
| OpenTelemetry              | Jaeger traces + OTLP metrics path; health `/ready`                                           |
| Fully observable (minimum) | Three channels in `ADMIN_MONITORING.md` (traces, metrics, stdout logs) — **P1-2** doc sprint |


---

## 7. Grafana vs native tools vs query files

### 7.1 When Grafana adds value

- **Postgres dashboard:** Strong fit — SQL over domain tables is the right abstraction for “message status” and billing-friendly ops view.
- **Unified entry** for OpenMRS admins (one URL, folder, refresh).
- **Correlation** (future): trace-to-metrics links if configured.

### 7.2 When Jaeger or Prometheus alone is better


| Tool                    | Better for                                                                           |
| ----------------------- | ------------------------------------------------------------------------------------ |
| **Jaeger UI**           | Trace search, span detail, tag `appointment.uuid`, service graph — **already works** |
| **Prometheus UI**       | Ad-hoc PromQL, alert state, metric name discovery — **avoids stale dashboard JSON**  |
| **RabbitMQ management** | Queue depth (gap **P1-4** — not in Grafana; documented limitation)                   |


### 7.3 Query file approach (recommended complement)

Add a repo file such as `observability/prometheus/queries.md` (name flexible) containing:

- Canonical **exported** metric names (from §4.1)
- Copy-paste PromQL for each assignment theme (ingest, dispatch, delivery, failures, pending, alerts)
- Note on scheduler timing for demos
- Link to `docs/ADMIN_MONITORING.md` Jaeger steps

**Does not replace** assignment “real-time dashboard” if Postgres + partial Prometheus Grafana are kept; it makes assessment **fair** when Prometheus JSON lags export names.

### 7.4 Decision matrix (project choices)


| Strategy                                                                | Fits assignment §4?                  | Maintenance | Assessor clarity                   |
| ----------------------------------------------------------------------- | ------------------------------------ | ----------- | ---------------------------------- |
| Keep all 3 Grafana dashboards + fix Prom/Jaeger JSON                    | Yes (strongest)                      | Medium      | High if fixed                      |
| Keep Postgres + Prometheus Grafana; drop Jaeger Grafana; doc Jaeger URL | Yes (traces via OTEL)                | Low         | High — no broken panel             |
| Postgres Grafana only + `queries.md` + Jaeger/Prometheus UI             | Yes (status/errors/throughput split) | Low         | Medium — explain two UIs           |
| No Grafana; only Jaeger + Prometheus + SQL in `DASHBOARD_DATABASE.md`   | Weak for “dashboard” wording         | Lowest      | Depends on assessor interpretation |


**Pragmatic path for this repo:** Fix Prometheus panel names (**§4.4 A**), fix or remove Jaeger Grafana dashboard (**§5.3 C or A**), add **queries.md** (**§7.3**), leave Postgres dashboard as-is.

---

## 8. Concrete change backlog (if implementing fixes)

Priority for dashboard accuracy only (not duplicating full gap plan sprints):

1. **PROM-1** — Update all expressions in `notification-module-prometheus-dashboard.json` to §4.1 exported names.
2. **PROM-2** — Align `notification_rules.yml` and any docs/scripts still using `notification_delivery_failure_total`, `scheduler_due_notifications_count`, etc.
3. **JAEGER-1** — Replace Jaeger dashboard Traces+search with Table search + `service` field, or remove dashboard and add README/ADMIN_MONITORING pointer.
4. **DOC-1** — Add `observability/prometheus/queries.md` (canonical PromQL + validation steps).
5. **OPTIONAL** — Panels for `delivery_tracking_writes_total`, scheduler histograms; RabbitMQ exporter (**P3-1** deferred in gap plan).

Sprint C items (**P1-2**, **P2-5**) in gap plan cover **documentation and Jaeger UI verification** — largely **done** in `docs/ADMIN_MONITORING.md`; they do **not** fix provisioned Grafana Jaeger JSON.

---

## 9. Cross-reference: gap analysis items vs this research


| Gap ID | Topic                     | Status in gap plan | Status for dashboards (this doc)                                                                        |
| ------ | ------------------------- | ------------------ | ------------------------------------------------------------------------------------------------------- |
| P0-2   | PII in Grafana SQL        | Done               | Postgres dashboard OK                                                                                   |
| P0-3   | Received/failed metrics   | Done               | Received panel OK when named correctly; failed panel needs failures + possible `_messages_total` suffix |
| P1-1   | PromQL `sum by`           | Done               | Aggregation OK; **metric names still wrong** on several panels                                          |
| P1-4   | Queue depth / DLQ         | Partial            | Not in any Grafana dashboard (by design)                                                                |
| P1-6   | Error table               | Done               | Postgres dashboard OK                                                                                   |
| P2-4   | Smoke test / metric names | Partial            | Smoke test uses `*_deliveries_total`; dashboard/alerts lag                                              |
| P2-5   | E2E Jaeger verify         | Sprint C           | **Jaeger UI** OK; **Grafana Jaeger dashboard** not OK                                                   |
| P3-1   | RabbitMQ exporter         | Deferred           | N/A                                                                                                     |


---

## 10. References


| Path                                                                                                                                           | Role                                                     |
| ---------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------- |
| `[OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md](OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md)`                                                     | Master gap list and sprints (do not edit from this work) |
| `[docs/ADMIN_MONITORING.md](docs/ADMIN_MONITORING.md)`                                                                                         | Admin runbook, Jaeger trace steps                        |
| `[docs/SPRINT_B_INSPECTION.md](docs/SPRINT_B_INSPECTION.md)`                                                                                   | Short Sprint B checklist                                 |
| `[DASHBOARD_DATABASE.md](DASHBOARD_DATABASE.md)`                                                                                               | SQL for Postgres panels                                  |
| `[observability/grafana/dashboards/*.json](observability/grafana/dashboards/)`                                                                 | Provisioned dashboards                                   |
| `[src/NotificationModule.Shared/Observability/NotificationTelemetry.cs](src/NotificationModule.Shared/Observability/NotificationTelemetry.cs)` | Instrument definitions and units                         |
| `[scripts/smoke-test-metrics.sh](scripts/smoke-test-metrics.sh)`                                                                               | Automated metrics validation                             |


---

*Document version: 1.0 — dashboard research and options; complements gap analysis without modifying it.*