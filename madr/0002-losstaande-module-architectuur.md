# ADR: Losstaande module-architectuur

## Status

Accepted

## Context

Een module voor OpenMRS kan op verschillende manieren uitgewerkt worden. Eén daarvan is als ingebouwde module, bijvoorbeeld als een `.omod`. Het kan echter ook als een losstaande module worden opgezet: een zelfstandig systeem dat niet afhankelijk is van het OpenMRS-knooppunt.

## Besluit

We zullen een losstaande module maken.

Een ingebouwde module zorgt door _tight coupling_ voor moeilijkere schaalbaarheid en maakt de oplossing te afhankelijk van een OpenMRS-instantie. Door de module als zelfstandig systeem op te zetten, vermijden we deze problemen.

## Gevolgen

### Positief

- Betere schaalbaarheid.
- Minder risico dat een storing in de messaging-module het volledige systeem (OpenMRS) beïnvloedt.

### Negatief

- Introduceert een extra fysiek component in de systeemarchitectuur dat apart beheerd en gemonitord moet worden.
