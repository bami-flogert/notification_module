# Sprint B inspection guide

Short checklist to review **Sprint B — Operability** changes. Full operations guide: [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md).

## What changed

| Area | Files |
|------|--------|
| Health checks | `RabbitMqHealthCheck.cs`, producer/consumer `Program.cs`, `docker-compose.yml` (consumer port `5002`) |
| Failure metrics | `NotificationWorker.cs`, `NotificationTelemetry.cs`, Prometheus dashboard JSON |
| Error dashboard | `notification-module-dashboard.json`, `DASHBOARD_DATABASE.md` |
| Docs | This file, `ADMIN_MONITORING.md`, `README.md` |

## 5-minute verification

### 1. Build

```bash
dotnet build src/NotificationModule.Producer/NotificationModule.Producer.csproj
dotnet build src/NotificationModule.Consumer/NotificationModule.Consumer.csproj
```

### 2. Start stack

```bash
docker compose --env-file env.example up --build -d
```

### 3. Health and readiness

```bash
curl -sf http://localhost:5001/health   # expect 200
curl -sf http://localhost:5001/ready    # expect 200
curl -sf http://localhost:5002/health   # expect 200
curl -sf http://localhost:5002/ready    # expect 200
```

**Degraded readiness test** (optional):

```bash
docker compose stop postgres
curl -s -o /dev/null -w "producer ready: %{http_code}\n" http://localhost:5001/ready   # expect 503
curl -s -o /dev/null -w "producer health: %{http_code}\n" http://localhost:5001/health # expect 200
docker compose start postgres
```

### 4. Metrics smoke test

```bash
./scripts/smoke-test-metrics.sh
```

Script waits on `/ready` for producer and consumer, then asserts delivery and received metrics in Prometheus.

### 5. Grafana error panel

1. Open http://localhost:3000 → **Notification Module** dashboard.
2. Confirm panel **Recent Delivery Failures (last 1h)** exists (may be empty until a failure occurs).
3. After a failed provider delivery, a row should appear within ~30s with `error_message` and `appointment_uuid` (no patient name).

### 6. Prometheus failure_reason

In Prometheus → Graph:

```promql
sum by (queue, failure_reason) (rate(notification_messages_failed_total[5m]))
```

After invalid JSON on a queue (advanced): series with `failure_reason="deserialize"`. After provider failure: `failure_reason="dispatch"`.

### 7. Privacy

```bash
rg -i "PatientName|patient_name" observability/grafana/
```

No matches in dashboard SQL panels.

## Admin quick reference

| Need | Where |
|------|--------|
| Recent errors with messages | Grafana → **Recent Delivery Failures (last 1h)** |
| Is the app ready to serve? | `curl http://localhost:5001/ready` and `:5002/ready` |
| Trace one appointment | Jaeger → tag `appointment.uuid` |
| Alert meanings | [`ADMIN_MONITORING.md`](ADMIN_MONITORING.md) § Prometheus alerts |

## DLQ note

Sprint B adds `failure_reason` on metrics only. Messages **nacked without requeue are not stored** in a dead-letter queue yet — see gap analysis doc P1-4 “better” path.
