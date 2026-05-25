# Teamoverzicht implementatie

Korte samenvatting van wat nog nodig is op basis van `assignment.md` en `IMPLEMENTATIE_TEKORTEN.md`.

## Wat staat al goed

- End-to-end flow werkt: FHIR intake -> planning -> RabbitMQ -> providers -> delivery tracking.
- 24u/1u reminders, annuleren/wijzigen, observability (Grafana/Prometheus/Loki/Jaeger) en Docker setup zijn aanwezig.
- Basis tests en CI draaien.

## Grootste open punten

- Notificatie-inhoud: locatie, instructies en lokale tijdzone worden nog niet goed in alle provider-berichten gebruikt.
- Privacy/security: 14-dagen data-opruiming ontbreekt, 1-jaar PII-vrije facturatiemetadata ontbreekt, en log-redactie is nog niet overal toegepast.
- Tijdzones: org-timezone staat in de DB maar wordt nog onvoldoende gebruikt voor planning/berichttekst.
- OpenMRS beheerintegratie: er ontbreekt een praktische stap-voor-stap beheerdersgids (2.7+).
- Deliverables missen deels: C4 niveau 3, procesflow-diagram, formeel testrapport en projectlog.

## Prioriteit voor komende sprint

1. Notificatiebericht verbeteren (locatie + instructies + lokale tijd).
2. Retentie implementeren (14 dagen) + facturatiemetadata (1 jaar, zonder PII).
3. OpenMRS integratiedocumentatie afronden voor beheerders.
4. C4 L3 + procesflow + kort testrapport opleveren.

## Concrete taakverdeling (voorstel)

- Backend A: message formatter + provider adapters aanpassen.
- Backend B: retention/background job + metadata model.
- Docs: OpenMRS integratiehandleiding + security hardening sectie.
- Architecture/QA: C4 L3, process flow, testbewijs en eindrapportage.

## Referenties

- Detailanalyse: `IMPLEMENTATIE_TEKORTEN.md`
- Engelse versie: `IMPLEMENTATION_GAPS.md`
