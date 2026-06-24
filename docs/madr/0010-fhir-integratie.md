# ADR: FHIR R4 voor afspraak-intake

## Status

Accepted

## Context

OpenMRS 2.7 en nieuwer kan afspraken via FHIR delen. De opdracht vraagt om aansluiting op OpenMRS en een duidelijke integratielaag voor beheerders.

We hadden al een eigen JSON-endpoint (`POST /api/appointments`). Dat werkt, maar elke koppeling moet dan onze veldnamen en foutformaat leren. In de zorg wordt FHIR steeds vaker gebruikt om systemen te laten samenwerken zonder maatwerk per koppeling.

Ook moet duidelijk zijn wanneer een afspraak **echt is opgeslagen** in onze database, en wanneer alleen is aangegeven dat verwerking later gebeurt.

## Besluit

We gebruiken **FHIR R4** met het resource-type `**Appointment`** als intake op `POST /fhir/Appointment` voor **FHIR-clients** en handmatige/tests.

De **primaire OpenMRS 3.0-koppeling** is de Notification Bridge OMOD die een JSON-webhook POST naar `POST /api/webhooks/openmrs/appointments/{organizationKey}` stuurt (zie [ADR 0011](0011-openmrs-omod-bridge.md)). De OMOD kan desgewenst FHIR2 intern gebruiken om contactgegevens te verrijken, maar de payload naar onze module is het vastgelegde webhook-contract.

Waarom FHIR:

- Sluit aan bij OpenMRS 2.7+ en bestaande FHIR-modules.
- Eén bekend formaat voor afspraak, patiëntverwijzing, status en tijd (`start`).
- Fouten kunnen als `**OperationOutcome**` terug, passend bij FHIR-clients.
- Het verouderde JSON-endpoint blijft beschikbaar voor tests en oude koppelingen.

**HTTP-status bij succesvolle FHIR-intake** (zoals geïmplementeerd in `FhirAppointmentController`):


| Situatie                      | Status        | Reden                                                                                  |
| ----------------------------- | ------------- | -------------------------------------------------------------------------------------- |
| Nieuwe afspraak opgeslagen    | `201 Created` | Resource is nieuw aangemaakt; `Location`-header wijst naar `/fhir/Appointment/{uuid}`. |
| Bestaande afspraak bijgewerkt | `200 OK`      | Resource bestond al; inhoud is bijgewerkt.                                             |


We gebruiken **geen** `202 Accepted` op het FHIR-pad: de afspraak en geplande herinneringen worden **direct** in PostgreSQL gezet. De client hoeft niet te raden of opslag gelukt is — dat volgt uit `201`/`200` en het `Bundle` in het antwoord.

Het legacy JSON-endpoint (`POST /api/appointments`) blijft `202 Accepted` gebruiken; dat gedrag is niet gewijzigd.

Dit besluit vervangt [ADR 0008](0008-delivery-acknowledgements.md) (status **Superseded**), dat nog uitging van alleen REST zonder FHIR en overal `202` voor intake.

## Gevolgen

### Positief

- OpenMRS-beheerders kunnen standaard FHIR-documentatie en tooling volgen.
- Duidelijke scheiding: FHIR = `201`/`200` + `OperationOutcome`; legacy JSON = `202` + JSON-body.
- Minder verwarring over “geaccepteerd” versus “opgeslagen”.

### Negatief

- Extra mapping- en validatielaag (`FhirAppointmentMapper`, extensions voor telefoon/e-mail/locatie).
- Clients moeten `application/fhir+json` ondersteunen op het FHIR-pad.
- Twee intake-paden blijven onderhouden tot het legacy-endpoint verwijderd wordt.

## Zie ook

- `[FHIR_ENDPOINT.md](../FHIR_ENDPOINT.md)` — endpoints, voorbeelden, statuscodes
- `[APPOINTMENT_ENDPOINT.md](../APPOINTMENT_ENDPOINT.md)` — legacy JSON (`202 Accepted`)

