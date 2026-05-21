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

De producer en consumer exporteren traces en metrics via OTLP.

- OTLP collector endpoint (default lokaal): `http://localhost:4317`
- In Docker: producer/consumer sturen naar `http://otel-collector:4317`
- Grafana: [http://localhost:3000](http://localhost:3000)
- Jaeger UI: [http://localhost:16686](http://localhost:16686)
- Prometheus UI: [http://localhost:9090](http://localhost:9090)

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

Zie [`OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md`](OBSERVABILITY_GAP_ANALYSIS_AND_FIX_PLAN.md) voor de volledige observability-audit, gap-analyse en geprioriteerde fix-backlog. Zie [`DASHBOARD_DATABASE.md`](DASHBOARD_DATABASE.md) voor DB data-flow en dashboard-SQL.

Admin monitoring (Engels): [`docs/ADMIN_MONITORING.md`](docs/ADMIN_MONITORING.md). Sprint B verificatie: [`docs/SPRINT_B_INSPECTION.md`](docs/SPRINT_B_INSPECTION.md).

Health endpoints: producer `http://localhost:5001/health` (liveness) en `/ready` (Postgres + RabbitMQ); consumer `http://localhost:5002/health` en `/ready`.

Metrieken smoke-test (Docker-stack moet draaien of wordt gestart door het script):

```bash
./scripts/smoke-test-metrics.sh
```

## Voorbeeldrequest

De producer exposeert `POST /api/appointments`. Deze endpoint bewaart de afspraak in PostgreSQL en maakt geplande notificatieregels aan voor later. De scheduler in de producer publiceert notificaties naar RabbitMQ zodra ze verzonden moeten worden. De consumer schrijft daarna per provider een delivery-resultaat naar PostgreSQL.

Voorbeeld met `curl`:

```bash
curl -X POST http://localhost:5001/api/appointments \
  -H "Content-Type: application/json" \
  -d '{
    "appointmentUuid": "demo-1",
    "organizationKey": "default",
    "patientUuid": "patient-1",
    "patientName": "Peter Jansen",
    "patientPhone": "+31612345678",
    "patientEmail": "Peter.jansen@example.com",
    "startDateTime": "2026-05-12T14:30:00Z",
    "status": "Confirmed",
    "location": "Polikliniek A, kamer 12",
    "instructions": "Neem een geldig identiteitsbewijs mee."
  }'
```

Verwachte response:

```json
{
  "message": "Appointment saved.",
  "appointmentUuid": "demo-1",
  "organizationKey": "default",
  "pendingNotifications": 2
}
```

Zie [`APPOINTMENT_ENDPOINT.md`](APPOINTMENT_ENDPOINT.md) voor uitleg over organisaties, de requestopties, scheduler, delivery tracking en welke tabellen worden gevuld.

## Geheimen (PostgreSQL)

| Omgevingsvariabele | Doel |
|--------------------|------|
| `SECRETS_MASTER_KEY_BASE64` | 32 bytes random key, Base64 → `Secrets__MasterKeyBase64` in de container |
| `SECRETS_SEED_*` | Eénmalige seed als `provider_secrets` nog leeg is (zie `env.example`) |
| `POSTGRES_PASSWORD` | Wachtwoord voor gebruiker `notification` (DB + connection string) |

Gebruik in productie een sterke master key (`openssl rand -base64 32`) en roteer/reseed volgens jullie beveiligingsbeleid.

### Unit tests

```bash
dotnet test NotificationModule.sln
```

GitHub Actions runs the same tests and verifies that the producer and consumer Docker images build on every push and pull request to `main`/`master` (see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

### Snelle smoke-test (Docker aan)

```bash
./scripts/smoke-test.sh
```
