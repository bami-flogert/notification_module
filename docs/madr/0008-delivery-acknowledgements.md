# ADR: Bevestigingen (ACK) voor notificaties

## Status

Superseded — vervangen door [0010-fhir-integratie.md](0010-fhir-integratie.md)

> Dit document beschrijft de eerdere keuze (alleen REST, intake `202 Accepted`). De module gebruikt inmiddels **FHIR R4** voor intake met `**201 Created`** / `**200 OK**`. Zie ADR 0010 en `[FHIR_ENDPOINT.md](../../FHIR_ENDPOINT.md)`. Het besluit en en de gevolgen zijn historisch.

## Context

De applicatie verwerkt afspraken via een REST-interface en verstuurt notificaties asynchroon via een berichtenqueue. Omdat geen gebruik werd gemaakt van HL7 v2- of FHIR-messaging, leek een volledig medisch ACK/NACK-protocol niet nodig.

## Besluit

De applicatie gebruikte een vereenvoudigd bevestigingsmodel met drie niveaus:

- **Intakebevestiging**  
De API retourneerde `HTTP 202 Accepted` zodra een afspraak correct was ontvangen.
- **Verwerkingsbevestiging**  
De queue bevestigde wanneer een notificatie succesvol was verwerkt of definitief was mislukt.
- **Applicatiebevestiging**  
De uiteindelijke status werd opgeslagen in `notification_deliveries`, inclusief status, foutmeldingen en tijdstippen.

Monitoring en logging verliepen via bestaande observability-componenten. Er werd geen apart HL7/FHIR ACK-mechanisme toegevoegd.

## Gevolgen

### Positief

- Centrale registratie van afleverstatussen.
- Minder architecturale complexiteit.
- Goed passend bij een REST-gebaseerd notificatiesysteem.

### Negatief

- Geen ondersteuning voor HL7/FHIR ACK-standaarden.
- Externe systemen ontvingen geen protocolniveau-ACK’s.
- Foutafhandeling bleef afhankelijk van logging en queue-statussen.

