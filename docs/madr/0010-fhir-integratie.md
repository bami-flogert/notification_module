# ADR: FHIR R4 voor afspraak-intake

## Status

Superseded → [0011](0011-openmrs-omod-bridge.md)

## Context

OpenMRS 2.7 en nieuwer kan afspraken via FHIR delen. We onderzochten FHIR R4 als intake-formaat en implementeerden dat tijdelijk naast een legacy JSON-endpoint.

Sinds ADR 0011 is de **primaire** integratie de OpenMRS Notification Bridge OMOD met een JSON-webhook naar `POST /api/webhooks/openmrs/appointments/{organizationKey}`. FHIR- en legacy-intake zijn verwijderd.

## Besluit (historisch)

We gebruikten **FHIR R4** met `Appointment` op `POST /fhir/Appointment`. Dat pad is vervangen door het OMOD-webhook-contract.

## Gevolgen

Zie [ADR 0011](0011-openmrs-omod-bridge.md) voor het huidige besluit.

## Zie ook

- [OMOD_BRIDGE.md](../openmrs/OMOD_BRIDGE.md)

