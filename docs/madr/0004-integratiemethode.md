# ADR: Integratiemethode

## Status

Accepted

## Context

De integratie met OpenMRS kan op verschillende manieren gebeuren:

- een directe verbinding met de SQL-database;
- een verbinding met de API;
- het gebruik van webhooks en event-modules.

De eerste twee opties zijn _pull-based_, terwijl de laatste optie _push-based_ is.

## Besluit

We kiezen voor een _push-based_ integratie via webhooks.

Een directe verbinding met de database zorgt voor te veel _tight coupling_, terwijl een verbinding via de API minder efficiënt kan zijn en vertraging kan veroorzaken.

## Gevolgen

### Positief

- _Loose coupling_ tussen systemen.
- De communicatiemodule wordt direct aangeroepen wanneer een event plaatsvindt, wat essentieel is voor reminders die snel verzonden moeten worden.

### Negatief

- De verzendende partij (OpenMRS) moet geconfigureerd worden om webhooks te ondersteunen en te versturen.
