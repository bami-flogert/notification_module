# Notification Module

Deze oplossing bestaat uit een producer, een consumer, een shared model en RabbitMQ als message broker. Provider-credentials worden **versleuteld** in **PostgreSQL** bewaard (AES-256-GCM); de **master key** komt alleen uit de omgeving (niet uit code of `appsettings`).

## Opstarten

Kopieer [`env.example`](env.example) naar `.env` en pas waarden aan, **of** gebruik `--env-file` (aanbevolen):

```bash
docker compose --env-file env.example up --build
```

Hiermee worden o.a. RabbitMQ, PostgreSQL, FakeComWorld (`comworld`), de producer en de consumer gestart. De consumer leest `Secrets__MasterKeyBase64` en `SecretsSeed__*` uit de omgeving; bij een **lege** secrets-tabel worden die waarden één keer **versleuteld** weggeschreven naar Postgres.

**Lokaal (zonder Docker):** zorg dat Postgres draait en zet `SecretsDb:ConnectionString`, `Secrets:MasterKeyBase64` en eventueel `SecretsSeed:*` (of vul de DB handmatig).

## Observability (OpenTelemetry)

De producer en consumer exporteren traces, metrics en logs via OTLP.

- OTLP collector endpoint (default lokaal): `http://localhost:4317`
- In Docker: producer/consumer sturen naar `http://otel-collector:4317`
- Grafana: [http://localhost:3000](http://localhost:3000) — Explore → **loki** voor logs (log → trace via `trace_id`)
- Loki API: [http://localhost:3100](http://localhost:3100)
- Jaeger UI: [http://localhost:16686](http://localhost:16686)
- Prometheus UI: [http://localhost:9090](http://localhost:9090)

ADR's: [`docs/madr/README.md`](docs/madr/README.md) (overzicht) · OpenMRS-bridge: [`docs/madr/0011-openmrs-omod-bridge.md`](docs/madr/0011-openmrs-omod-bridge.md) · Observability: [`docs/madr/0007-opentelemetry-logs-with-loki.md`](docs/madr/0007-opentelemetry-logs-with-loki.md)

Belangrijke metrics:

- `appointments_ingested_total`
- `scheduled_notifications_created_total`
- `scheduled_notifications_published_total`
- `notification_dispatch_total`
- `notification_dispatch_duration_ms`
- `delivery_tracking_writes_total`
- `scheduler_cycle_duration_ms`
- `scheduler_due_notifications_count`

Provisioned dashboards:

- `Notification Module` (PostgreSQL operationeel overzicht)
- `Notification Module - Prometheus Metrics`
- `Notification Module - Jaeger Traces`

Zie [`docs/DASHBOARD_DATABASE.md`](docs/DASHBOARD_DATABASE.md) voor DB data-flow en dashboard-SQL.

Health endpoints: producer `http://localhost:5001/health` (liveness) en `/ready` (Postgres + RabbitMQ); consumer `http://localhost:5002/health` en `/ready`.

## Voorbeeldrequest (OpenMRS OMOD webhook)

De primaire OpenMRS-koppeling is een JSON-webhook van de Notification Bridge OMOD:

```bash
curl -X POST http://localhost:5001/api/webhooks/openmrs/appointments/default \
  -H "X-Api-Key: change-me-in-prod" \
  -H "Content-Type: application/json" \
  -d '{
    "event": "CREATED",
    "appointmentUuid": "openmrs-appointment-123",
    "status": "Scheduled",
    "startDateTime": "2026-06-24T09:00:00Z",
    "endDateTime": "2026-06-24T09:30:00Z",
    "patientUuid": "openmrs-patient-456",
    "patientName": "John Doe",
    "patientPhone": "+31612345678",
    "patientEmail": "john@example.com",
    "service": "General Medicine",
    "location": "Outpatient",
    "comments": "Neem een geldig identiteitsbewijs mee."
  }'
```

Zie [`docs/openmrs/OMOD_BRIDGE.md`](docs/openmrs/OMOD_BRIDGE.md).

## Documentatie

| Onderwerp | Bestand |
|-----------|---------|
| OpenMRS webhook-intake | [`docs/openmrs/OMOD_BRIDGE.md`](docs/openmrs/OMOD_BRIDGE.md) |
| Providerbeleid per organisatie (`PUT /api/organizations/{key}/providers`) | [`docs/DASHBOARD_DATABASE.md`](docs/DASHBOARD_DATABASE.md) (organisatieconfig) · API-key via `X-Api-Key` |
| Billingrapport (`GET /api/reports/deliveries`) | [`docs/DASHBOARD_DATABASE.md`](docs/DASHBOARD_DATABASE.md) (Billing deliveries report API) |
| Uitbreiden (provider, nieuw meldingtype, tekenset) | [`docs/EXTENSIBILITY.md`](docs/EXTENSIBILITY.md) |
| Betrouwbaarheid (DLQ, retries) | [`docs/RELIABILITY.md`](docs/RELIABILITY.md) |
| Testrapportage (betrouwbaarheid & uitbreidbaarheid) | [`docs/TESTRAPPORT.md`](docs/TESTRAPPORT.md) |
| Performancerapportage (throughput & monitoring) | [`docs/PERFORMANCERAPPORT.md`](docs/PERFORMANCERAPPORT.md) |
| Architectuur (C4 diagrammen) | [`docs/c4/expl.md`](docs/c4/expl.md) |
| OpenMRS OMOD-bridge | [`docs/openmrs/OMOD_BRIDGE.md`](docs/openmrs/OMOD_BRIDGE.md) |
| Requirements traceability | [`docs/REQUIREMENTS_TRACEABILITY.md`](docs/REQUIREMENTS_TRACEABILITY.md) |
| Testontwerp (FMEA) | [`docs/TESTONTWERP_FMEA.md`](docs/TESTONTWERP_FMEA.md) |
| FMEA | [`docs/fmea/FMEA.md`](docs/fmea/FMEA.md) |
| Productie-deployment en TLS 1.3 | [`docs/PRODUCTION_SECURITY.md`](docs/PRODUCTION_SECURITY.md) |
| Architectuurbeslissingen | [`docs/madr/README.md`](docs/madr/README.md) |

## Geheimen (PostgreSQL)

| Omgevingsvariabele          | Doel                                                                     |
| --------------------------- | ------------------------------------------------------------------------ |
| `SECRETS_MASTER_KEY_BASE64` | 32 bytes random key, Base64 → `Secrets__MasterKeyBase64` in de container |
| `SECRETS_SEED_*`            | Eénmalige seed als `provider_secrets` nog leeg is (zie `env.example`)    |
| `APIKEY_SEED_DEFAULT`       | Eénmalige seed voor webhook-auth (wordt gehashed opgeslagen) |
| `POSTGRES_PASSWORD`         | Wachtwoord voor gebruiker `notification` (DB + connection string)        |

Gebruik in productie een sterke master key (`openssl rand -base64 32`) en roteer/reseed volgens jullie beveiligingsbeleid.

### Unit tests

```bash
dotnet test NotificationModule.sln
```

GitHub Actions runs the same tests and verifies that the producer and consumer Docker images build on every push and pull request to `main`/`master` (see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

### Comprehensive test checklist

Full matrix (endpoints, DB, observability, pipeline): see [docs/TEST_CHECKLIST.md](docs/TEST_CHECKLIST.md).

```powershell
docker compose --env-file env.example up --build -d
dotnet test NotificationModule.sln
.\scripts\comprehensive-test.ps1
```

```bash
./scripts/comprehensive-test.sh
```

### Snelle smoke-test (Docker aan)

```bash
./scripts/smoke-test.sh
```
