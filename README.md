# Notification Module

Deze oplossing bestaat uit een producer, een consumer, een shared model en RabbitMQ als message broker.

## Opstarten

Start de volledige stack met Docker Compose:

```powershell
docker compose up --build
```

Daarmee worden onder andere RabbitMQ, de producer en de consumer gestart.

## Voorbeeldrequest

De producer exposeert `POST /api/appointments`.

Voorbeeld met `curl`:

```bash
curl -X POST http://localhost:5001/api/appointments \
  -H "Content-Type: application/json" \
  -d '{
    "appointmentUuid": "demo-1",
    "patientUuid": "patient-1",
    "patientName": "Peter Jansen",
    "patientPhone": "+31612345678",
    "patientEmail": "Peter.jansen@example.com",
    "startDateTime": "2026-05-12T14:30:00Z",
    "status": "Confirmed"
  }'
```

Verwachte response:

```json
{
  "message": "Notification queued.",
  "appointmentUuid": "demo-1"
}
```

