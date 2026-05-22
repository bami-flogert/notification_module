# Notification Module

Deze oplossing bestaat uit een producer, een consumer, een shared model en RabbitMQ als message broker. Provider-credentials worden **versleuteld** in **PostgreSQL** bewaard (AES-256-GCM); de **master key** komt alleen uit de omgeving (niet uit code of `appsettings`).

## Opstarten

Kopieer [`env.example`](env.example) naar `.env` en pas waarden aan, **of** gebruik `--env-file` (aanbevolen):

```bash
docker compose --env-file env.example up --build
```

Hiermee worden o.a. RabbitMQ, PostgreSQL, FakeComWorld (`comworld`), de producer en de consumer gestart. De consumer leest `Secrets__MasterKeyBase64` en `SecretsSeed__*` uit de omgeving; bij een **lege** secrets-tabel worden die waarden één keer **versleuteld** weggeschreven naar Postgres.

**Lokaal (zonder Docker):** zorg dat Postgres draait en zet `SecretsDb:ConnectionString`, `Secrets:MasterKeyBase64` en eventueel `SecretsSeed:*` (of vul de DB handmatig).

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

## Geheimen (PostgreSQL)

| Omgevingsvariabele | Doel |
|--------------------|------|
| `SECRETS_MASTER_KEY_BASE64` | 32 bytes random key, Base64 → `Secrets__MasterKeyBase64` in de container |
| `SECRETS_SEED_*` | Eénmalige seed als `provider_secrets` nog leeg is (zie `env.example`) |
| `APIKEY_SEED_DEFAULT` | Eénmalige seed voor `POST /api/appointments` auth (wordt gehashed opgeslagen) |
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
