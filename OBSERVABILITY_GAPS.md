# Observability Gap Analysis

Dit bestand beschrijft welke observability-onderdelen in de huidige codebase nog ontbreken of onvoldoende zijn uitgewerkt ten opzichte van `opdracht.md`.

## Huidige situatie (kort)

- Er is basis logging via `ILogger` in producer en consumer.
- Er is een Grafana dashboard dat data uit PostgreSQL leest.
- Er zijn Docker healthchecks voor `rabbitmq` en `postgres` (in `docker-compose.yml`).

## Wat er nog ontbreekt

### 1) OpenTelemetry implementatie ontbreekt

De opdracht noemt expliciet monitoringtooling "bijvoorbeeld op basis van OpenTelemetry". In de code ontbreken:

- OTel SDK configuratie (`AddOpenTelemetry`, resource/service metadata).
- Distributed traces tussen:
  - ingest API (`POST /api/appointments`),
  - scheduler,
  - RabbitMQ publish/consume,
  - provider calls,
  - database writes.
- OTel metrics (counters/histograms/up-down counters).
- Exporters (bijv. OTLP/Prometheus/Jaeger/Tempo) en bijbehorende pipeline.

### 2) Metrics voor throughput en fouten ontbreken

De opdracht vraagt realtime zicht op throughput, status en fouten. Er zijn nu geen expliciete applicatie-metrics zoals:

- aantal ontvangen appointments;
- aantal geplande notifications;
- aantal gepubliceerde RabbitMQ berichten;
- queue lag / age van due notifications;
- aantal succesvolle/falende provider deliveries per provider;
- retry counts, dead-letter counts;
- end-to-end latency (ingest -> delivery).

### 3) Realtime operationeel dashboard is nog beperkt

Het huidige dashboard is vooral DB-overzicht en geen volwaardige operationele observability-view:

- geen tijdreeks-grafieken voor throughput/latency/error-rate;
- geen p95/p99 latency panelen;
- geen queue depth/consumer lag panelen;
- geen service health panelen per component (producer/consumer/rabbitmq/provider integrations);
- geen drilldown op incidenten met correlatie-id/trace-id.

### 4) Alerting ontbreekt

Er is geen zichtbare alerting-configuratie aanwezig (Grafana Alerting / Alertmanager / andere tooling), o.a. voor:

- hoge foutpercentages per provider;
- vastlopende scheduler of consumer;
- oplopende queue backlog;
- uitzonderlijk hoge delivery-latency;
- uitval van afhankelijkheden (RabbitMQ/PostgreSQL/provider API).

### 5) Health/readiness endpoints voor applicaties ontbreken

`producer` en `consumer` hebben geen expliciete health/readiness endpoints op applicatieniveau:

- geen `AddHealthChecks()` en endpoint mapping;
- geen dependency checks voor DB en RabbitMQ vanuit app-context;
- geen readiness onderscheid (app gestart vs echt klaar voor verkeer).

### 6) Correlatie en structured logging kan beter

Er is logging aanwezig, maar voor troubleshooting/audit op systeemniveau ontbreken nog:

- consistente correlation-id per berichtflow;
- trace-id/span-id verrijking in logs;
- meer gestandaardiseerde structured log velden (organization, appointmentUuid, scheduledNotificationId, provider, outcome, duration);
- log-level en log-retentie strategie voor productie.

### 7) SLO/SLI-definities ontbreken

Er zijn geen expliciete reliability doelen en meetdefinities, zoals:

- SLI voor delivery success rate;
- SLI voor tijdige aflevering (24h/1h reminders op tijd verstuurd);
- SLO targets per tenant/provider;
- error budget afspraken en rapportage.

### 8) Incident- en auditgericht observability-beleid ontbreekt

Voor operationele beheerbaarheid missen nog duidelijke afspraken/documentatie over:

- incident response runbooks (wat te doen bij provider outage of backlog);
- standaard dashboards per rol (beheerder, operations, facturatie);
- dataclassificatie in observability (geen gevoelige data in logs/telemetry);
- bewaartermijnen voor logs/metrics/traces passend bij security/privacy eisen.

## Aanbevolen minimale aanvulling (MVP)

Om snel aan de observability-eisen te voldoen:

1. Voeg OpenTelemetry toe aan producer + consumer (traces + metrics + OTLP export).
2. Voeg health/readiness endpoints toe met checks op DB + RabbitMQ.
3. Definieer 8-12 kernmetrics (throughput, errors, latency, queue/backlog).
4. Breid Grafana uit met tijdreeks-panelen en fout/latency-overzicht per provider.
5. Configureer minimaal 5 alerts (error rate, backlog, scheduler stil, consumer stil, provider failure spike).
6. Leg SLI/SLO’s en incident-runbook vast in documentatie.
