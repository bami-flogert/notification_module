# ADR: OpenMRS 3.0 integratie via Notification Bridge OMOD

## Status

Accepted

## Context

De opdracht vereist integratie met OpenMRS **zonder wijzigingen aan OpenMRS core**. Eerder werd een aangepaste OpenMRS-fork gebruikt; dat leidt tot hoge onderhoudskosten en breekt bij platform-upgrades.

De Communicatiemodule is een **losstaande** .NET-service (zie [ADR 0002](0002-losstaande-module-architectuur.md)). Integratie moet **push-based** zijn (zie [ADR 0004](0004-integratiemethode.md)): zodra een afspraak wordt aangemaakt, gewijzigd of geannuleerd, moet de module direct worden geïnformeerd.

OpenMRS 3.0 biedt Appointment Scheduling en FHIR2, maar FHIR2 is een **pull-API** — geen webhooks. Er is geen standaard OMOD die afspraak-events naar een externe notificatieservice pusht.

## Besluit

We bouwen een **eigen dunne OMOD** (`notification-bridge`) die:

1. Luistert naar appointment create/update/cancel via de Appointment Scheduling API-laag (Advice of module listener), **zonder core-patches**.
2. Een **JSON webhook** POST naar de Communicatiemodule:
   - `POST /api/webhooks/openmrs/appointments/{organizationKey}`
   - Payload-contract: [OMOD_BRIDGE.md](../openmrs/OMOD_BRIDGE.md)
3. Gebruik maakt van een **outbox-tabel** en retry/backoff wanneer de Producer niet bereikbaar is.

De Communicatiemodule mapt de webhook naar het interne `AppointmentMessage`-model (`OpenMrsWebhookMapper`) en hergebruikt `AppointmentIngestionService` voor idempotente opslag en reminder-planning.

Eerdere intake-paden (legacy JSON en FHIR) zijn verwijderd; zie [ADR 0010](0010-fhir-integratie.md) (Superseded).

## Gevolgen

### Positief

- Geen fork van OpenMRS; module is installeerbaar/verwijderbaar via Admin UI.
- Push past bij reminder-timing (lage latentie).
- Losse schaalbaarheid van de Communicatiemodule blijft behouden.
- Outbox + Producer-retries tonen resiliency end-to-end.

### Negatief

- Extra artefact (Java OMOD) naast de .NET-stack; aparte build en release.
- Contactgegevens (telefoon/e-mail) moeten in de OMOD worden verrijkt als Appointment Scheduling die niet standaard levert.

## Zie ook

- [INTEGRATION_POINTS.md](../openmrs/INTEGRATION_POINTS.md)
- [OMOD_BRIDGE.md](../openmrs/OMOD_BRIDGE.md)
- [REQUIREMENTS_TRACEABILITY.md](../REQUIREMENTS_TRACEABILITY.md)
