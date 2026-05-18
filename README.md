# Notification Module

Deze oplossing bestaat uit een producer, een consumer, een shared model en RabbitMQ als message broker. Provider-credentials worden **versleuteld** in **PostgreSQL** bewaard (AES-256-GCM); de **master key** komt alleen uit de omgeving (niet uit code of `appsettings`).

## Opstarten

Kopieer [`env.example`](env.example) naar `.env` en pas waarden aan, **of** gebruik `--env-file` (aanbevolen):

```bash
docker compose --env-file env.example up --build
```

Hiermee worden o.a. RabbitMQ, PostgreSQL, FakeComWorld (`comworld`), de producer en de consumer gestart. De consumer leest `Secrets__MasterKeyBase64` en `SecretsSeed__*` uit de omgeving; bij een **lege** secrets-tabel worden die waarden één keer **versleuteld** weggeschreven naar Postgres.

**Lokaal (zonder Docker):** zorg dat Postgres draait en zet `SecretsDb:ConnectionString`, `Secrets:MasterKeyBase64` en eventueel `SecretsSeed:*` (of vul de DB handmatig).

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

### Snelle smoke-test (Docker aan)

```bash
./scripts/smoke-test.sh
```
