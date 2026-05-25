# Betrouwbaarheid en retries

Dit document beschrijft hoe de notificatiemodule omgaat met fouten. Issue #13 breidt dit bestand uit met scheduler-retry, provider-HTTP-retry, fallback-republish en herstel van verouderde `Publishing`-status.

## Dead-letter queues (DLQ)

Als een bericht na het toegestane aantal pogingen niet verwerkt kan worden, gaat het naar een **dead-letter queue** in plaats van te verdwijnen.

### Wachtrijnamen

| Provider-wachtrij | Dead-letter queue |
|-------------------|-------------------|
| `notifications.swiftsend` | `notifications.swiftsend.dlq` |
| `notifications.securepost` | `notifications.securepost.dlq` |
| `notifications.legacylink` | `notifications.legacylink.dlq` |
| `notifications.asyncflow` | `notifications.asyncflow.dlq` |

De topologie wordt bij opstarten gedeclareerd door producer en consumer (`RabbitMqTopology` in `NotificationModule.Shared`).

### Wanneer een bericht naar de DLQ gaat

| Situatie | Retries | DLQ-reden header (`x-dlq-reason`) |
|----------|---------|-------------------------------------|
| JSON kan niet worden gedeserialiseerd naar `AppointmentMessage` | Geen (direct) | `deserialize` |
| Verwerking gooit een exception | Maximaal 3 deliveries (`x-retry-count` 0 → 1 → 2) | `max_retries` |

**Niet** naar de DLQ: provider-dispatch mislukt maar het bericht is wel afgehandeld (delivery geregistreerd, optionele fallback naar andere provider). Dat pad is bewust zo ontworpen.

### Retry-gedrag

Bij een verwerkingsfout met `x-retry-count` &lt; 2 publiceert de consumer dezelfde body opnieuw naar exchange `appointment.notifications` met een verhoogde `x-retry-count` en bevestigt het oorspronkelijke bericht. Bij de derde mislukking (`x-retry-count` ≥ 2) gaat het bericht naar de DLQ en wordt het origineel bevestigd.

### Herstel door beheerder (replay vanuit DLQ)

1. Open **RabbitMQ Management** (standaard: `http://localhost:15672`, guest/guest in dev).
2. Open de relevante DLQ, bijv. `notifications.swiftsend.dlq`.
3. **Get messages** en inspecteer payload en `x-dlq-reason`.
4. Los het onderliggende probleem op (ongeldige JSON, ontbrekende org-secrets, provider-storing, enz.).
5. **Republish** naar de hoofdflow:
   - Exchange: `appointment.notifications`
   - Routing key: providernaam (`SwiftSend`, `SecurePost`, `LegacyLink` of `AsyncFlow`)
   - Verwijder of reset `x-retry-count` als je een nieuw retry-budget wilt.
6. **Acknowledge of delete** het DLQ-bericht na een succesvolle replay.

### Alerting

Prometheus-metriek: `notification_messages_dlq_total` (tags: `queue`, `provider`, `reason`). Een aanhoudend tarief &gt; 0 betekent dat berichten aandacht van een beheerder nodig hebben. Zie het Grafana-dashboardpaneel **Messages Dead-Lettered Rate**.

## Gerelateerd

- ADR: [0009-dead-letter-queues.md](madr/0009-dead-letter-queues.md), [0010-fhir-integratie.md](madr/0010-fhir-integratie.md)
- Implementatie: `NotificationWorker`, `RabbitMqDeadLetterPublisher`, `RabbitMqMessageFailurePolicy`
