## 2. Bevestigingen (ACK) voor notificaties

### Status

Accepted

### Context

De applicatie verwerkt afspraken via een REST-interface en verstuurt notificaties asynchroon via een berichtenqueue. Omdat geen gebruik wordt gemaakt van HL7 v2- of FHIR-messaging, is een volledig medisch ACK/NACK-protocol niet nodig.

### Beslissing

De applicatie gebruikt een vereenvoudigd bevestigingsmodel met drie niveaus:

- **Intakebevestiging**  
De API retourneert `HTTP 202 Accepted` zodra een afspraak correct is ontvangen.
- **Verwerkingsbevestiging**  
De queue bevestigt wanneer een notificatie succesvol is verwerkt of definitief is mislukt.
- **Applicatiebevestiging**  
De uiteindelijke status wordt opgeslagen in `notification_deliveries`, inclusief status, foutmeldingen en tijdstippen.

Monitoring en logging verlopen via bestaande observability-componenten. Er wordt geen apart HL7/FHIR ACK-mechanisme toegevoegd.

### Gevolgen

**Positief**

- Centrale registratie van afleverstatussen.
- Minder architecturale complexiteit.
- Goed passend bij een REST-gebaseerd notificatiesysteem.

**Negatief**

- Geen ondersteuning voor HL7/FHIR ACK-standaarden.
- Externe systemen ontvangen geen protocolniveau-ACK’s.
- Foutafhandeling blijft afhankelijk van logging en queue-statussen.

