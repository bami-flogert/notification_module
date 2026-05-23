# Reliability and retries

This document describes how the notification module handles failures. Issue #13 will extend this file with scheduler retry, provider HTTP retry, fallback republish, and stale `Publishing` recovery.

## Dead-letter queues (DLQ)

When a message cannot be processed after the allowed attempts, it is moved to a **dead-letter queue** instead of being discarded.

### Queue names

| Provider queue | Dead-letter queue |
|----------------|-------------------|
| `notifications.swiftsend` | `notifications.swiftsend.dlq` |
| `notifications.securepost` | `notifications.securepost.dlq` |
| `notifications.legacylink` | `notifications.legacylink.dlq` |
| `notifications.asyncflow` | `notifications.asyncflow.dlq` |

Topology is declared at startup by the producer and consumer (`RabbitMqTopology` in `NotificationModule.Shared`).

### When a message goes to the DLQ

| Situation | Retries | DLQ reason header (`x-dlq-reason`) |
|-----------|---------|-------------------------------------|
| JSON cannot be deserialized to `AppointmentMessage` | None (immediate) | `deserialize` |
| Processing throws an exception | Up to 3 deliveries (`x-retry-count` 0 → 1 → 2) | `max_retries` |

**Not** sent to the DLQ: provider dispatch failed but the message was handled (delivery recorded, optional fallback to another provider). That path is intentional.

### Retry behaviour

On a processing exception with `x-retry-count` &lt; 2, the consumer republishes the same body to exchange `appointment.notifications` with an incremented `x-retry-count` and acknowledges the original message. On the third failure (`x-retry-count` ≥ 2), the message is published to the DLQ and the original is acknowledged.

### Operator recovery (replay from DLQ)

1. Open **RabbitMQ Management** (default: `http://localhost:15672`, guest/guest in dev).
2. Open the relevant DLQ, e.g. `notifications.swiftsend.dlq`.
3. **Get messages** and inspect payload and `x-dlq-reason`.
4. Fix the underlying issue (invalid JSON, missing org secrets, provider outage, etc.).
5. **Republish** to the main flow:
   - Exchange: `appointment.notifications`
   - Routing key: provider name (`SwiftSend`, `SecurePost`, `LegacyLink`, or `AsyncFlow`)
   - Remove or reset `x-retry-count` if you want a fresh retry budget.
6. **Acknowledge or delete** the DLQ message after a successful replay.

### Alerting

Prometheus metric: `notification_messages_dlq_total` (tags: `queue`, `provider`, `reason`). A sustained rate &gt; 0 indicates messages need operator attention. See the Grafana dashboard panel **Messages Dead-Lettered Rate**.

## Related

- ADR: [0009-dead-letter-queues.md](madr/0009-dead-letter-queues.md), [0010-fhir-integratie.md](madr/0010-fhir-integratie.md) (Dutch)
- Implementation: `NotificationWorker`, `RabbitMqDeadLetterPublisher`, `RabbitMqMessageFailurePolicy`
