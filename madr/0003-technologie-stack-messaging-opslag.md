# ADR: Technologie-stack (Messaging & Opslag)

## Status

Accepted

## Context

Voor afspraakherinneringen is een betrouwbare messaging-oplossing en opslag voor audit-tracking nodig.

## Besluit

We gebruiken `RabbitMQ` voor messaging en een relationele database (SQL) voor opslag.

De volgende opties zijn overwogen maar afgewezen:

- `Kafka`: te hoge complexiteit voor de huidige workload. `RabbitMQ` is lichter en eenvoudiger te beheren.
- `MongoDB`: minder geschikt voor de strikte audit-tracking en relationele metadata die vereist zijn voor medische verslaglegging.

## Gevolgen

### Positief

- `RabbitMQ` biedt eenvoudige ondersteuning voor retries.
- SQL-opslag garandeert data-integriteit voor medische audit-logs.
