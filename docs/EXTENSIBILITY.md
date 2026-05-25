# Uitbreidbaarheid

Deze module is zo opgebouwd dat je later eenvoudig kunt uitbreiden: een vijfde berichtenkanal toevoegen, of een ander soort melding dan alleen afspraakherinneringen (bijvoorbeeld een labuitslag).

Het overzicht hieronder noemt **welke bestanden** je moet aanpassen. Werk altijd eerst lokaal met `docker compose` en `dotnet test` voordat je merge’t.

---

## Een vijfde berichtenprovider toevoegen

Voorbeeld: je wilt een provider **“SmsDirect”** naast SwiftSend, SecurePost, LegacyLink en AsyncFlow.

### Wat je doet in het kort

1. Schrijf een adapter die `INotificationProvider` implementeert.
2. Registreer die adapter in de consumer.
3. Voeg een RabbitMQ-wachtrij en routing key toe (plus DLQ).
4. Zet geheimen en configuratie klaar voor FakeComWorld of productie.
5. Test dat berichten op de juiste wachtrij landen en dat de dispatcher je provider kiest.

### Bestanden (checklist)

| Stap | Bestand | Wat je doet |
|------|---------|-------------|
| 1 | `src/NotificationModule.Consumer/Adapters/SmsDirectProvider.cs` (nieuw) | Implementeer `INotificationProvider`: eigenschap `ChannelName` (bijv. `"SmsDirect"`) en `SendAsync` met HTTP-aanroep naar de provider. |
| 2 | `src/NotificationModule.Consumer/Program.cs` | Registreer: `AddSingleton<INotificationProvider, SmsDirectProvider>()`. |
| 3 | `src/NotificationModule.Shared/Messaging/RabbitMqTopology.cs` | Voeg een regel toe aan `QueueBindings`, bijv. `("notifications.smsdirect", "SmsDirect")`. DLQ heet dan automatisch `notifications.smsdirect.dlq`. |
| 4 | `src/NotificationModule.Consumer/Workers/NotificationQueueMapping.cs` | Map wachtrijnaam → providernaam (bijv. `"smsdirect"` → `"SmsDirect"`). |
| 5 | `src/NotificationModule.Consumer/Secrets/ProviderSecretsStore.cs` + `SecretsInitializer.cs` | Ondersteuning voor opslaan en laden van API-keys of wachtwoorden voor de nieuwe provider (volg het patroon van SwiftSend of LegacyLink). |
| 6 | `env.example` | Documenteer nieuwe URL’s en seed-variabelen voor lokale tests. |
| 7 | `NotificationModule.Shared/Messaging/NotificationProviders.cs` | Voeg de naam toe aan `All` en `IsSupported`; daarna accepteert `PUT /api/organizations/{key}/providers` de nieuwe provider. |
| 8 | `tests/NotificationModule.Tests/...` | Unit test voor queue-mapping; eventueel een test met een nep-`HttpClient` in de adapter (zie `SwiftSendProviderMessageIdTests`). |

**Niet** nodig: wijzigingen in `NotificationDispatcher.cs` — die pikt alle geregistreerde providers automatisch op.

### Configuratie

In `appsettings` of omgeving, zelfde patroon als bestaande providers:

```json
"Providers": {
  "SmsDirect": { "BaseUrl": "http://comworld:8080" }
}
```

### RabbitMQ

- Exchange: `appointment.notifications` (direct).
- Routing key = exact de `ChannelName` (bijv. `SmsDirect`).
- De scheduler publiceert met `TargetProvider` = voorkeursprovider van de organisatie.

---

## Een nieuw type melding toevoegen (bijv. labuitslag)

Nu draait alles om **afspraken** (`AppointmentMessage`, FHIR `Appointment`, herinneringen 24u/1u). Een labuitslag vraagt een **eigen pad**: ander FHIR-resource, andere tekst, andere wachtrij, andere planning.

### Wat je doet in het kort

1. Definieer een nieuw berichtmodel (of breid het huidige voorzichtig uit).
2. Bouw intake aan de producer-kant (FHIR-endpoint of aparte route).
3. Sla data op (nieuwe tabel of kolommen + migratie).
4. Plan of verstuur via een eigen scheduler of worker.
5. Voeg een consumer-worker of uitbreiding die naar de juiste provider-wachtrij publiceert.

### Bestanden (checklist)

| Onderdeel | Bestanden | Wat je doet |
|-----------|-----------|-------------|
| Model | `src/NotificationModule.Shared/Models/` (nieuw, bijv. `LabResultMessage.cs`) | Velden die je nodig hebt: organisatie, patiëntverwijzing, resultaattekst, geen overbodige PII in logs. |
| Database | `NotificationDbContext.cs`, nieuwe migration in `Shared/Migrations/` | Tabel voor lab-aanvragen + eventueel `scheduled_notifications` met een `notification_type` of aparte tabel. |
| FHIR intake | `Controllers/` + `Fhir/` (nieuw, bijv. `FhirObservationController.cs`, mapper, validator) | Map FHIR `Observation` (of ander resource) naar `LabResultMessage`. |
| Ingest | Nieuw service-klasse naar voorbeeld van `AppointmentIngestionService.cs` | Valideer, sla op, maak geplande rijen aan. |
| Scheduler | `NotificationSchedulerWorker.cs` of nieuwe `LabResultSchedulerWorker` | Publiceer naar RabbitMQ wanneer het tijd is om te versturen. |
| RabbitMQ | `RabbitMqTopology.cs` | Nieuwe exchange of routing keys, bijv. `lab.notifications` + wachtrij per provider. |
| Consumer | Nieuwe worker of uitbreiding van `NotificationWorker` | Lees de lab-wachtrij, roep dezelfde `INotificationProvider`-adapters aan met aangepaste tekst. |
| Tekst | `NotificationMessageBuilder` (issue #1) of aparte builder | Eén plek voor de SMS-tekst van labuitslagen. |
| Docs | `FHIR_ENDPOINT.md` of nieuw `LAB_ENDPOINT.md` | Hoe OpenMRS (of een ander systeem) moet posten. |

Dit is meer werk dan alleen een vijfde provider; plan het als een kleine epic met eigen tests.

---

## Tekenset (character encoding)

### Wat we ondersteunen

| Onderdeel | Tekenset |
|-----------|----------|
| FHIR-intake (`POST /fhir/Appointment`) | **UTF-8** — body is JSON (`application/fhir+json`). |
| Legacy JSON-intake (`POST /api/appointments`) | **UTF-8** — standaard voor ASP.NET JSON. |
| Berichten op RabbitMQ | **UTF-8** — JSON-body. |
| SwiftSend, SecurePost, AsyncFlow | **UTF-8** in JSON-body. |
| LegacyLink | **UTF-8** — XML begint met `<?xml version="1.0" encoding="utf-8"?>`. |
| PostgreSQL | UTF-8 (standaard bij Npgsql). |

Gebruik in namen en teksten gewone Unicode (Nederlands, emoji in testomgeving, enz.) zolang alles UTF-8 blijft.

### Wat clients moeten doen

- Stuur FHIR met `Content-Type: application/fhir+json` (liefst **zonder** een andere charset in de header, of expliciet `charset=utf-8`).
- Sla bestanden niet op als Latin-1 of Windows-1252 en stuur ze dan als “JSON” — dat geeft kapotte tekens of parsefouten.

### Fout bij verkeerde of onleesbare bytes

- **`Content-Type` met andere charset dan UTF-8** (bijv. `charset=iso-8859-1`): HTTP **400** met `OperationOutcome` — *“Only UTF-8 is supported…”*
- **Body geen geldige UTF-8-bytes:** HTTP **400** met `OperationOutcome` — *“Request body is not valid UTF-8…”*
- **Ongeldige JSON / FHIR:** HTTP **400** met `OperationOutcome` (fouttekst in `diagnostics`).
- **Lege body:** ook **400** met `OperationOutcome`.

Implementatie: `FhirRequestEncoding` en `FhirAppointmentController` in de producer.

---

## Gerelateerde documentatie

- [RELIABILITY.md](RELIABILITY.md) — retries, DLQ
- [FHIR_ENDPOINT.md](FHIR_ENDPOINT.md) — afspraak-intake
- [madr/0010-fhir-integratie.md](madr/0010-fhir-integratie.md) — waarom FHIR
