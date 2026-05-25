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

ADR's: [`docs/madr/README.md`](docs/madr/README.md) (overzicht) · FHIR-intake: [`docs/madr/0010-fhir-integratie.md`](docs/madr/0010-fhir-integratie.md) · Observability: [`docs/madr/0007-opentelemetry-logs-with-loki.md`](docs/madr/0007-opentelemetry-logs-with-loki.md)

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

Zie [`DASHBOARD_DATABASE.md`](DASHBOARD_DATABASE.md) voor DB data-flow en dashboard-SQL.

Health endpoints: producer `http://localhost:5001/health` (liveness) en `/ready` (Postgres + RabbitMQ); consumer `http://localhost:5002/health` en `/ready`.

## Voorbeeldrequest (FHIR)

De producer accepteert FHIR R4 `Appointment` resources op `POST /fhir/Appointment` (HL7 ACK via `OperationOutcome` in het antwoord). Zie [`FHIR_ENDPOINT.md`](FHIR_ENDPOINT.md).

```bash
curl -X POST http://localhost:5001/fhir/Appointment/default \
  -H "X-Api-Key: change-me-in-prod" \
  -H "Content-Type: application/fhir+json" \
  -H "Accept: application/fhir+json" \
  -d '{
    "resourceType": "Appointment",
    "status": "booked",
    "start": "2026-05-12T14:30:00Z",
    "identifier": [{ "system": "http://openmrs.org/appointment", "value": "demo-1" }],
    "participant": [{
      "actor": { "reference": "Patient/patient-1", "display": "Peter Jansen" },
      "status": "accepted"
    }],
    "patientInstruction": "Neem een geldig identiteitsbewijs mee.",
    "extension": [
      { "url": "http://notification-module.local/StructureDefinition/patient-phone", "valueString": "+31612345678" },
      { "url": "http://notification-module.local/StructureDefinition/patient-email", "valueString": "peter@example.com" },
      { "url": "http://notification-module.local/StructureDefinition/location-text", "valueString": "Polikliniek A" }
    ]
  }'
```

Het platte JSON-endpoint `POST /api/appointments` blijft beschikbaar maar is verouderd; zie [`APPOINTMENT_ENDPOINT.md`](APPOINTMENT_ENDPOINT.md).

## Documentatie

| Onderwerp | Bestand |
|-----------|---------|
| FHIR-intake | [`FHIR_ENDPOINT.md`](FHIR_ENDPOINT.md) |
| Providerbeleid per organisatie (`PUT /api/organizations/{key}/providers`) | [`DASHBOARD_DATABASE.md`](DASHBOARD_DATABASE.md) (organisatieconfig) · API-key via `X-Api-Key` |
| Billingrapport (`GET /api/reports/deliveries`) | [`DASHBOARD_DATABASE.md`](DASHBOARD_DATABASE.md) (Billing deliveries report API) |
| Uitbreiden (provider, nieuw meldingtype, tekenset) | [`docs/EXTENSIBILITY.md`](docs/EXTENSIBILITY.md) |
| Betrouwbaarheid (DLQ, retries) | [`docs/RELIABILITY.md`](docs/RELIABILITY.md) |
| Productie-deployment en TLS 1.3 | [`docs/PRODUCTION_SECURITY.md`](docs/PRODUCTION_SECURITY.md) |
| Architectuurbeslissingen | [`docs/madr/README.md`](docs/madr/README.md) |

## Geheimen (PostgreSQL)

| Omgevingsvariabele          | Doel                                                                     |
| --------------------------- | ------------------------------------------------------------------------ |
| `SECRETS_MASTER_KEY_BASE64` | 32 bytes random key, Base64 → `Secrets__MasterKeyBase64` in de container |
| `SECRETS_SEED_*`            | Eénmalige seed als `provider_secrets` nog leeg is (zie `env.example`)    |
| `APIKEY_SEED_DEFAULT`       | Eénmalige seed voor `POST /api/appointments` auth (wordt gehashed opgeslagen) |
| `POSTGRES_PASSWORD`         | Wachtwoord voor gebruiker `notification` (DB + connection string)        |

Gebruik in productie een sterke master key (`openssl rand -base64 32`) en roteer/reseed volgens jullie beveiligingsbeleid.

### Unit tests

```bash
dotnet test NotificationModule.sln
```

GitHub Actions runs the same tests and verifies that the producer and consumer Docker images build on every push and pull request to `main`/`master` (see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

### Comprehensive test checklist

Full matrix (endpoints, DB, observability, pipeline): see [TEST_CHECKLIST.md](TEST_CHECKLIST.md).

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
