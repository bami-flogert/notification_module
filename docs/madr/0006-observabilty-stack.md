# ADR: Observability Stack
## Status
Accepted
## Context
De Notification Module draait als losstaand systeem met een producer, een consumer en een achtergrond-scheduler. Om problemen te kunnen opsporen — zoals berichten die niet worden verstuurd, trage verwerking of afleverpogingen die mislukken — is inzicht in traces, metrics en logs vereist.
Overwogen alternatieven:
- **Datadog / New Relic:** kant-en-klare SaaS-oplossingen, maar commercieel en kostbaar bij een groeiend volume. Geen volledige controle over de data.
- **ELK-stack (Elasticsearch + Logstash + Kibana):** biedt goede log-aggregatie, maar heeft geen ingebouwde ondersteuning voor gedistribueerde traces en vereist aanzienlijk meer beheer dan de gekozen stack.
- **Alleen gestructureerde logging (stdout):** eenvoudig, maar biedt geen metrics, geen trace-correlatie en geen centraal dashboard.
## Besluit
We gebruiken **OpenTelemetry** als vendor-neutrale instrumentatielaag, gecombineerd met de volgende open-source backends:
| Signaal | Backend |
|---------|---------|
| Traces | **Jaeger** |
| Metrics | **Prometheus** |
| Logs | **Loki** |
| Dashboards | **Grafana** |
De producer en consumer sturen traces, metrics en logs via OTLP naar een centrale **OpenTelemetry Collector**. De collector routeert elk signaaltype naar de bijbehorende backend. Grafana fungeert als enkel dashboard voor alle drie de signaaltypen en maakt directe navigatie van een log-regel naar de bijbehorende trace mogelijk via `trace_id`.
## Gevolgen
### Positief
- Alle drie de observability-signalen (traces, metrics, logs) zijn in één Grafana-interface te raadplegen.
- OpenTelemetry is vendor-neutraal; de backend kan worden vervangen zonder wijzigingen aan de applicatiecode.
- Jaeger, Prometheus en Loki zijn gratis en draaien volledig on-premise, wat geen externe dataverwerking vereist.
### Negatief
- De stack bestaat uit vijf extra services (OpenTelemetry Collector, Jaeger, Prometheus, Loki, Grafana), wat de `docker-compose.yml` en het beheer complexer maakt.
- Lokale opslag van Loki en Jaeger is vluchtig; voor productie is persistente opslag vereist.
