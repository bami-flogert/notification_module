# Metrics Verification Checklist

This document describes quick checks to verify the newly implemented metrics and health endpoints for the Notification Module.

Prerequisites
- Docker stack (Postgres, RabbitMQ, OTLP collector, Prometheus) running as in `docker-compose.yml`.
- Producer and Consumer built and running locally (or in Docker).

Endpoints
- Producer health: `http://localhost:<producer-port>/health`
- Producer ready: `http://localhost:<producer-port>/ready`
- Metrics: via OTLP collector or Prometheus scrape (no direct `/metrics` endpoint in app unless configured)

Quick verification steps

1) Health endpoints

- Curl the health endpoints:

```bash
curl -f http://localhost:5000/health || echo "Producer health failed"
curl -f http://localhost:5000/ready || echo "Producer ready failed"
```

Expect HTTP 200 when DB and RabbitMQ are reachable.

2) Scheduler queue metrics

- Start the Producer and ensure scheduler runs.
- Inspect the OTLP/Prometheus target (Prometheus UI) and query:

```
notification_pending_count
notification_pending_oldest_seconds
scheduler_due_notifications_count
```

Expect numbers to reflect DB contents; `notification_pending_oldest_seconds` should be 0 when no pending items.

3) Delivery outcome counters and latency

- Produce a test appointment that causes a scheduled notification to be published and delivered by a provider.
- Use Prometheus UI or `promtool` to query:

```
notification_delivery_success_total{provider="SwiftSend"}
notification_delivery_failure_total{provider="SwiftSend"}
notification_end_to_end_latency_seconds_bucket
```

- Alternatively inspect OTLP collector logs to confirm metrics exported.

4) Simulate failures and retries

- Force a provider failure (e.g., configure provider to return 500) and verify that `notification_delivery_failure_total` increments with `error_type` tag set appropriately.

5) Run the automated smoke test

- Use the new smoke test script to exercise the end-to-end flow and verify metrics in Prometheus:

```bash
bash scripts/smoke-test-metrics.sh
```

- The script starts the stack, posts a sample appointment, and waits for `notification_delivery_success_total` to appear in Prometheus.

6) Logs and tracing

- Each delivery record now logs debug-level when latency metric recording fails.
- Traces should contain `consumer.delivery.record` activity with tags: `scheduled_notification.id`, `appointment.uuid`, `provider`, `delivery.status`.

6) Troubleshooting

- If metrics do not appear, verify the OTLP endpoint environment variable `OpenTelemetry__Otlp__Endpoint` or Prometheus scraping configuration in `observability/otel` and `observability/prometheus`.
- Ensure `NotificationTelemetry` meter name `NotificationModule` is present in OTLP payload.

7) Commands to simulate local flow

- Run the producer and consumer locally (example using `dotnet run` in each project folder):

```bash
cd src/NotificationModule.Producer
dotnet run

cd ../NotificationModule.Consumer
dotnet run
```

- Use test script or Postman to create an appointment to trigger the flow.

If you'd like, I can add a small integration test or a Postman collection to automate these checks.
