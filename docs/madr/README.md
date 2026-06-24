# Architecture Decision Records

Log of significant architectural decisions for the Notification Module (Michael Nygard format).

Sjabloon: [ADR-Sjabloon.md](ADR-Sjabloon.md)

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-intro.md) | Introductie ADR's | Accepted |
| [0002](0002-losstaande-module-architectuur.md) | Losstaande module-architectuur | Accepted |
| [0003](0003-technologie-stack-messaging-opslag.md) | Technologie-stack (messaging & opslag) | Accepted |
| [0004](0004-integratiemethode.md) | Integratiemethode | Accepted |
| [0005](0005-dotnet-voor-de-module.md) | .NET voor de module | Accepted |
| [0006](0006-observabilty-stack.md) | Observability-stack | Accepted |
| [0007](0007-opentelemetry-logs-with-loki.md) | OpenTelemetry logs met Loki | Accepted |
| [0008](0008-delivery-acknowledgements.md) | Bevestigingen (ACK) voor notificaties | Superseded → [0010](0010-fhir-integratie.md) |
| [0009](0009-dead-letter-queues.md) | Dead-letter queues voor mislukte berichten | Accepted |
| [0010](0010-fhir-integratie.md) | FHIR R4 voor afspraak-intake | Accepted |
| [0011](0011-openmrs-omod-bridge.md) | OpenMRS 3.0 integratie via Notification Bridge OMOD | Accepted |
