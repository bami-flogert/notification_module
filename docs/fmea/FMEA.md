# FMEA - Communicatiemodule

Per component kijken we wat er mis kan gaan, wat het gevolg is en hoe we dat hebben opgelost in de code.

Traceability naar tests: [TESTONTWERP_FMEA.md](../TESTONTWERP_FMEA.md) · Requirements: [REQUIREMENTS_TRACEABILITY.md](../REQUIREMENTS_TRACEABILITY.md)

---

## OpenMRS Notification Bridge OMOD

### Producer endpoint down of timeout

* **Effect:** afspraak-event bereikt de Communicatiemodule niet; geen reminders
* **Oorzaak:** netwerk, Producer herstart, TLS-fout
* **Maatregel:** outbox-tabel in OMOD; rijen blijven `PENDING` tot HTTP `2xx`; exponential backoff (zie [OMOD_BRIDGE.md](../openmrs/OMOD_BRIDGE.md))

### Dubbele webhook voor dezelfde afspraak

* **Effect:** dubbele notificaties bij patiënt
* **Oorzaak:** retry in OMOD na timeout terwijl Producer wél heeft opgeslagen; dubbele events in OpenMRS
* **Maatregel:** upsert op `(organization_id, appointment_uuid)` in `AppointmentIngestionService`; `FOR UPDATE SKIP LOCKED` bij scheduler

### Event-volgorde: update vóór cancel of omgekeerd

* **Effect:** verkeerde reminder-status (bijv. cancel overschreven door late update)
* **Oorzaak:** asynchrone HTTP, outbox-replay
* **Maatregel:** `event: CANCELLED` forceert cancel-logica; laatste succesvolle POST wint op UUID; documenteer ordering-beperking in demo

### Ontbrekende contactgegevens in webhook

* **Effect:** notificatie kan niet worden afgeleverd via SMS/e-mail
* **Oorzaak:** Appointment Scheduling levert geen telefoon/e-mail; OMOD stuurt alleen basisvelden
* **Maatregel:** OMOD verrijkt optioneel via Patient-API; Consumer logt `Failed` in `notification_deliveries`; FMEA-test T-OMOD-04

### Ongeldige payload (ontbrekende UUID of datum)

* **Effect:** 400 van Producer; event blijft in outbox of wordt als poison gemarkeerd
* **Oorzaak:** mappingfout in OMOD
* **Maatregel:** `OpenMrsWebhookMapper` valideert verplichte velden; OMOD logt 4xx en markeert outbox `FAILED` (geen eindeloze retry)

---

## Producer API

### Ongeldige afspraken worden geaccepteerd

* **Effect:** er worden notificaties aangemaakt voor afspraken die niet kloppen
* **Oorzaak:** geen validatie op verplichte velden of datum in het verleden
* **Maatregel:** afspraken in het verleden worden direct genegeerd, reminders die al verstreken zijn ook

```csharp
// AppointmentIngestionService.cs
if (startDateTime <= now)
    return;

var scheduledSendAt = startDateTime.Subtract(definition.OffsetBeforeAppointment);
if (scheduledSendAt <= now)
    continue;
```

---

### Afspraak wordt dubbel ingediend

* **Effect:** patient krijgt dubbele notificaties
* **Oorzaak:** OpenMRS stuurt hetzelfde request meerdere keren
* **Maatregel:** we gebruiken `FOR UPDATE SKIP LOCKED` zodat hetzelfde bericht nooit twee keer wordt opgepakt, ook niet bij meerdere draaiende instances

```sql
-- NotificationSchedulerWorker.cs
SELECT "Id" FROM scheduled_notifications
WHERE "Status" = 'Pending' AND "ScheduledSendAt" <= {now}
ORDER BY "ScheduledSendAt"
LIMIT {batchSize}
FOR UPDATE SKIP LOCKED
```

---

### Scheduler publiceert een bericht niet of te laat

* **Effect:** patient ontvangt de notificatie niet op tijd
* **Oorzaak:** scheduler crasht of publicatie naar RabbitMQ mislukt
* **Maatregel:** status blijft `Pending` tot het echt gelukt is, mislukte publicaties worden automatisch teruggedraaid zodat de scheduler het opnieuw probeert

```csharp
// NotificationSchedulerWorker.cs
try
{
    _publisher.Publish(message);
    scheduledNotification.Status = ScheduledNotificationStatuses.Queued;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to publish; reverting to Pending.");
    scheduledNotification.Status = ScheduledNotificationStatuses.Pending;
}
```

Berichten die te lang in `Publishing` hangen worden ook automatisch teruggezet:

```csharp
await db.ScheduledNotifications
    .Where(x => x.Status == "Publishing" && x.UpdatedAt < staleBefore)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Pending"));
```

---

### RabbitMQ is tijdelijk niet beschikbaar

* **Effect:** bericht wordt niet gepubliceerd
* **Oorzaak:** RabbitMQ container herstart of netwerkfout
* **Maatregel:** `EnsureConnectedWithRetry()` blijft proberen te verbinden totdat het lukt

```csharp
// RabbitMqPublisher.cs
private void EnsureConnectedWithRetry(int delayMs = 3000)
{
    if (_connection?.IsOpen == true && _channel?.IsOpen == true)
        return;

    while (true)
    {
        try
        {
            _connection = _factory.CreateConnection();
            _channel    = _connection.CreateModel();
            DeclareTopology();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ unavailable. Retrying in {DelaySeconds}s.", delayMs / 1000);
            Thread.Sleep(delayMs);
        }
    }
}
```

---

## RabbitMQ

### Berichten gaan verloren bij een herstart

* **Effect:** notificaties worden nooit verstuurd
* **Oorzaak:** queues of exchange niet persistent geconfigureerd
* **Maatregel:** alles wordt durable gedeclareerd en berichten worden persistent verstuurd

```csharp
// RabbitMqPublisher.cs + NotificationWorker.cs
channel.ExchangeDeclare(ExchangeName, ExchangeType.Fanout, durable: true);
channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false);

props.Persistent = true;
```

---

### RabbitMQ start later op dan de rest

* **Effect:** verbindingsfout bij opstarten
* **Oorzaak:** race condition in Docker Compose
* **Maatregel:** zowel Producer als Consumer hebben een retry-loop die elke 3 seconden opnieuw probeert

```csharp
// NotificationWorker.cs
while (!ct.IsCancellationRequested)
{
    try
    {
        _connection = factory.CreateConnection();
        _logger.LogInformation("Connected to RabbitMQ.");
        return;
    }
    catch (Exception ex)
    {
        _logger.LogWarning("RabbitMQ not ready: {Msg}. Retrying in 3s...", ex.Message);
        await Task.Delay(3000, ct);
    }
}
```

---

## Consumer / Dispatcher

### Provider-API is niet bereikbaar of geeft een fout

* **Effect:** notificatie komt niet aan bij de patient
* **Oorzaak:** tijdelijke downtime bij SwiftSend, LegacyLink, AsyncFlow of SecurePost
* **Maatregel:** het resultaat wordt per provider opgeslagen als `Sent` of `Failed` in `notification_deliveries`, zichtbaar in het dashboard

```csharp
// NotificationWorker.cs
var result = await _dispatcher.DispatchToProviderAsync(message, providerName, stoppingToken);

await _deliveryTracking.RecordAsync(
    message,
    providerName,
    result.Success,
    result.ErrorMessage,
    stoppingToken);
```

---

### Bericht kan niet worden gelezen (corrupt JSON)

* **Effect:** bericht wordt overgeslagen zonder verwerking
* **Oorzaak:** corruptie in de queue of incompatibele berichtversie
* **Maatregel:** `BasicNack` met `requeue: false` zodat het bericht de queue niet blokkeert

```csharp
// NotificationWorker.cs
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process message. Nacking.");
    channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
}
```

---

### Consumer crasht midden in een verwerking

* **Effect:** bericht blijft onbevestigd in de queue
* **Oorzaak:** onverwachte exception of container-herstart
* **Maatregel:** `autoAck: false` zodat een bericht pas bevestigd wordt na succesvolle verwerking, bij herstart pakt de Consumer het gewoon opnieuw op

```csharp
// NotificationWorker.cs
channel.BasicConsume(queue, autoAck: false, consumer: consumer);

// pas na succesvolle verwerking:
channel.BasicAck(ea.DeliveryTag, multiple: false);
```

---

### Verkeerde provider-secrets

* **Effect:** authenticatiefout bij provider, notificatie mislukt
* **Oorzaak:** fout in omgevingsvariabelen of seed-data bij opstarten
* **Maatregel:** secrets worden versleuteld opgeslagen via AES-256-GCM, de master key komt alleen uit de omgevingsvariabele en staat nooit in de code

```bash
# .env
SECRETS_MASTER_KEY_BASE64=<32-byte random key, nooit in code>
SECRETS_SEED_SWIFTSEND_API_KEY=<provider credential>
```

---

## PostgreSQL

### Database tijdelijk niet bereikbaar

* **Effect:** afspraken kunnen niet worden opgeslagen, scheduler kan niet pollen
* **Oorzaak:** container herstart of netwerkfout
* **Maatregel:** Docker Compose healthcheck zorgt dat Producer en Consumer pas starten als Postgres echt klaar is

```yaml
# docker-compose.yml
postgres:
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U notification -d notification"]
    interval: 5s
    retries: 10

producer:
  depends_on:
    postgres:
      condition: service_healthy
```

---

### Patientgegevens blijven langer dan 14 dagen bewaard

* **Effect:** schending van de privacy-eis
* **Oorzaak:** geen automatische opschoning
* **Maatregel:** `DataRetentionWorker` roept periodiek `DataRetentionPurge` aan; PII-velden worden gewist na `DATA_RETENTION_RETENTION_DAYS` (default 14), billing-meta maximaal 365 dagen

```csharp
// DataRetentionWorker.cs — hosted service in Producer
// DataRetentionPurge.cs — purge patient fields + oude billing events
```

---

### Gevoelige data in logbestanden

* **Effect:** privacy-lek in logs
* **Oorzaak:** patientgegevens per ongeluk gelogd
* **Maatregel:** `notification_deliveries` slaat nooit naam, telefoonnummer of e-mail op, alleen provider, status, tijdstip en `scheduledNotificationId`
