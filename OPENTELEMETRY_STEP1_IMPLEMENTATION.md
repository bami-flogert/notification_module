# OpenTelemetry Step 1 Implementation

Dit document beschrijft wat is geïmplementeerd voor Step 1 (traces + metrics + OTLP export) en bevat de gevraagde database-analyse van het huidige dashboard.

## Geïmplementeerd

### Producer + Consumer OpenTelemetry

- OpenTelemetry SDK en OTLP exporter toegevoegd aan:
  - `src/NotificationModule.Producer/NotificationModule.Producer.csproj`
  - `src/NotificationModule.Consumer/NotificationModule.Consumer.csproj`
- Resource metadata toegevoegd in beide services:
  - `service.name`
  - `service.version`
  - `deployment.environment`
- OTLP endpoint configureerbaar via:
  - `OpenTelemetry__Otlp__Endpoint`
  - `OpenTelemetry:Otlp:Endpoint` in appsettings

### Tracing

- ASP.NET request tracing in producer.
- Custom spans toegevoegd voor:
  - appointment ingestion,
  - scheduler publish-cyclus,
  - RabbitMQ publish,
  - RabbitMQ consume,
  - provider dispatch,
  - delivery tracking write.
- Trace context propagatie over RabbitMQ headers toegevoegd (inject in producer, extract in consumer).

### Metrics

Nieuwe metrics in `src/NotificationModule.Shared/Observability/NotificationTelemetry.cs`:

- `appointments_ingested_total`
- `scheduled_notifications_created_total`
- `scheduled_notifications_published_total`
- `notification_dispatch_total`
- `notification_dispatch_duration_ms`
- `delivery_tracking_writes_total`
- `scheduler_cycle_duration_ms`
- `scheduler_due_notifications_count`

### OTLP backend in Docker observability stack

Toegevoegd:

- `otel-collector` met OTLP receiver.
- `jaeger` voor traces.
- `prometheus` scraping van collector metrics endpoint.
- Grafana datasources voor Prometheus en Jaeger naast bestaande Postgres datasource.

Bestanden:

- `docker-compose.yml`
- `observability/otel/otel-collector-config.yml`
- `observability/prometheus/prometheus.yml`
- `observability/grafana/provisioning/datasources/postgres.yml`

## Database-analyse (gevraagde research)

### Wat Grafana nu uit de database leest

Gebaseerd op `observability/grafana/dashboards/notification-module-dashboard.json`:

- Upcoming appointments:
  - `appointments` + `organizations`
- Pending scheduled notifications:
  - `scheduled_notifications` + `appointments` + `organizations`
- Status samenvatting:
  - aggregaties op `scheduled_notifications` en `notification_deliveries`
- Latest deliveries:
  - `notification_deliveries` + `scheduled_notifications` + `appointments` + `organizations`
- Deliveries by provider:
  - aggregatie op `notification_deliveries`

### Wat de applicatie momenteel naar de database schrijft

- Producer:
  - `organizations` (aanmaken indien ontbrekend),
  - `appointments` (create/update),
  - `scheduled_notifications` (create/cancel/status).
- Scheduler:
  - statusupdates op `scheduled_notifications` (`Pending`, `Publishing`, `Queued`).
- Consumer:
  - `notification_deliveries` (upsert per provider),
  - statusupdates op `scheduled_notifications` (`Sent`, `Failed`).
- Secrets initialisatie:
  - `provider_secrets` en default organization records.

### Moet er extra naar de database worden opgeslagen voor OpenTelemetry?

Voor Step 1: **nee, niet nodig**.

- Traces en metrics horen naar OTLP backend te gaan (collector + tracing/metrics backend), niet in business-tabellen.
- De bestaande business-tabellen blijven geschikt voor operationele tabellen/facturatieoverzichten.
- Alleen als je lange-termijn KPI rapportages buiten telemetry-retentie nodig hebt, is een extra geaggregeerde rapportage-tabel/materialized view zinvol.
