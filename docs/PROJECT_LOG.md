# Realisatie-transparantie logboek

Dit logboek beschrijft welke tools het team heeft gebruikt tijdens de realisatie van de Notification Module, waarom ze zijn gekozen, en wat de toegevoegde waarde en kosten waren. Het bevat ook een overzicht van bijdragen per teamlid.

## Gebruikte IDE's

### JetBrains Rider

**Waarom Rider:**
- Uitstekende ondersteuning voor .NET 8 en ASP.NET Core.
- Ingebouwde database-inspector (handig voor het controleren van EF Core-migraties en PostgreSQL).
- Geïntegreerde Docker- en docker-compose-ondersteuning.
- Soepele navigatie in grote solution-structuren (meerdere projecten in één `.sln`).

**Toegevoegde waarde:**
- Refactoring-tools (bijv. renaming, extract method) versnelden het herstructureren van services.
- De ingebouwde HTTP-client maakte het snel testen van REST-endpoints mogelijk zonder externe tool.

**Kosten / aandachtspunten:**
- Rider is betaald (studenten- of JetBrains-licentie vereist).
- Hogere geheugengebruik dan lichtere editors; merkbaar op laptops met 8 GB RAM.

---

### Visual Studio 2022

**Waarvoor gebruikt:**
- Debuggen van de producer en consumer via de ingebouwde debugger met breakpoints en geheugeninspectie.
- Uitvoeren van EF Core-migraties via de Package Manager Console (`Add-Migration`, `Update-Database`).

**Waarom gekozen:**
- Standaard Microsoft-tooling voor .NET; diepste integratie met de .NET-runtime en Entity Framework.
- Gratis Community-editie beschikbaar.

**Toegevoegde waarde:**
- De visuele debugger maakte het eenvoudig om de staat van `ScheduledNotificationRecord`-objecten stap voor stap te inspecteren bij het oplossen van scheduler-bugs.

**Kosten / aandachtspunten:**
- Zware installatie (meerdere GB); opstarttijd is merkbaar langer dan bij Rider of Cursor.
- Minder geschikt als dagelijkse editor wanneer meerdere terminaltaken parallel lopen.

---

### Cursor (met Composer 2.5)

**Waarvoor gebruikt:**
- Schrijven van grotere wijzigingen over meerdere bestanden tegelijk via **Cursor Composer** (Claude-based multi-file editing).
- Snel genereren van testklassen op basis van bestaande service-code.
- Documentatie-edits waarbij Composer de context van omliggende bestanden meenam.

**Waarom gekozen:**
- Cursor Composer kan meerdere bestanden tegelijk aanpassen in één instructie, wat handig is bij het doorvoeren van een nieuw patroon (bijv. het toevoegen van `ProviderMessageId` aan zowel entity, service als migratie).
- Gebouwd op VS Code, dus bekende sneltoetsen en extensies.

**Toegevoegde waarde:**
- Tijdwinst bij cross-file refactoring: het toevoegen van een nieuw veld aan de delivery-tracking pipeline (issue #18) over vier bestanden heen kostte één Composer-instructie in plaats van vier handmatige bewerkingen.
- Composer 2.5 haalde de juiste context op uit gerelateerde bestanden zonder dat die handmatig geplakt hoefden te worden.

**Kosten / aandachtspunten:**
- Composer-output moest altijd handmatig gereviewed worden; bij grotere wijzigingen werden soms meer bestanden aangepast dan gevraagd.
- Gratis tier heeft een maandelijks limiet op Composer-aanroepen; bij intensief gebruik overschreden.
- Gegenereerde code gebruikte soms deprecated API's of ontbrak een `using`-statement; altijd compileren na acceptatie.

---

## Gebruikte AI-tools

### 1. GitHub Copilot

**Waarvoor gebruikt:**
- Aanvullen van boilerplate-code (EF Core entity-klassen, controller-skeletons, xUnit-testmethoden).
- Genereren van XML-documentatiecommentaar.
- Snelle suggesties bij herhaalpatronen (bijv. de vier provider-adapters hebben een identieke structuur die Copilot na de eerste kon aanvullen).

**Waarom gekozen:**
- Geïntegreerd in Rider via de JetBrains AI-plugin; geen context-switching nodig.
- Gratis voor studenten via GitHub Education.

**Toegevoegde waarde:**
- Tijdwinst bij repetitief werk: de LegacyLink XML-adapter (inclusief custom XML-parser) was in circa 30 minuten klaar, waar dit zonder Copilot eerder een uur zou kosten.
- Suggesties voor unit-testvarianten die het team zelf niet direct had bedacht.

**Kosten / aandachtspunten:**
- Suggesties voor minder gangbare bibliotheken (bijv. `Hl7.Fhir.R4`) waren soms verouderd of incorrect; altijd verificatie met de officiële documentatie nodig.
- Copilot genereerde soms onnodig complexe LINQ-queries; handmatige vereenvoudiging was vereist.
- De gegenereerde code voor AES-256-GCM-encryptie bevatte aanvankelijk een fout in de nonce-generatie die pas tijdens code review werd ontdekt.

**Voorbeeldprompt (redacted):**

> *"Generate a C# class `LegacyLinkProvider` that implements `INotificationProvider`. It should send an XML POST request using `HttpClient`. The XML body should contain fields: recipient phone, message body, and organization key. Extract the `<MessageReference>` element from the XML response and return it as the provider message ID."*

Resultaat: een werkend skelet met `XDocument`-parsing. Het team heeft de foutafhandeling en de credential-ophaling via `ProviderSecretsStore` handmatig toegevoegd.

---

### 2. Claude

**Waarvoor gebruikt:**
- Architectuurvragen en vergelijking van ontwerpopties (bijv. RabbitMQ direct exchange vs. topic exchange; EF Core vs. Dapper voor de scheduler-query).
- Opstellen en reviewen van FHIR R4-mappings en HL7-documentatie.
- Reviewen van beveiligingsimplementaties (AES-GCM, API key hashing).
- Opstellen van documentatie (ADR's, EXTENSIBILITY.md, RELIABILITY.md).
- Code-review van pull requests vóór merge.

**Waarom gekozen:**
- Sterker in beredeneerde antwoorden en afweging van alternatieven dan Copilot.
- Bruikbaar voor langere technische vragen waarbij context door meerdere bestanden heen nodig is.

**Toegevoegde waarde:**
- De keuze voor `FOR UPDATE SKIP LOCKED` in de scheduler-query (ter voorkoming van race conditions bij parallelle consumers) is beargumenteerd en uitgewerkt na een gesprek over concurrentie-risico's.
- De FHIR OperationOutcome ACK/NACK-structuur is correct opgezet dankzij uitleg over de HL7-specificatie.
- Tijdwinst bij het schrijven van ADR's: de structuur en motivatie waren in één iteratie bruikbaar.

**Kosten / aandachtspunten:**
- Claude heeft geen directe toegang tot de codebase; relevante code-fragmenten moesten handmatig in de prompt worden geplakt.
- Antwoorden over bibliotheekversies (bijv. `Hl7.Fhir.R4`) waren soms gebaseerd op verouderde informatie; altijd controleren via NuGet.
- Voor complexe multi-file-wijzigingen moest het gesprek meerdere rondes kosten om tot een consistent resultaat te komen.

**Voorbeeldprompt (redacted):**

> *"We bouwen een notification module voor OpenMRS. De scheduler pikt berichten op uit PostgreSQL en publiceert ze naar RabbitMQ. We hebben twee instanties van de producer draaien. Hoe voorkomen we dat dezelfde notificatie twee keer wordt gepubliceerd? Welke PostgreSQL-mechanismen zijn hiervoor geschikt?"*

Resultaat: uitleg van `SELECT ... FOR UPDATE SKIP LOCKED` als patroon, met een concreet SQL-voorbeeld dat vervolgens is vertaald naar de EF Core raw-SQL-aanroep in `NotificationSchedulerWorker`.

---

## Reflectie en verbeterpunten voor toekomstige projecten

### Wat goed werkte

- **Copilot voor herhaalpatronen:** Bij de vier provider-adapters versnelde Copilot het werk sterk zodra de eerste adapter als referentie beschikbaar was.
- **Claude voor architectuur en documentatie:** Iteratief documentatie opstellen via Claude kostte minder tijd dan volledig zelf schrijven, en de kwaliteit was direct bruikbaar als eerste versie.
- **Scheiding van verantwoordelijkheden in prompts:** Korte, gerichte prompts ("schrijf alleen de HTTP-aanroep, geen DI-registratie") leverden bruikbaardere output dan brede prompts.

### Wat beter kon

- **Versiebeheer van AI-gegenereerde code:** Geef in de commit-message aan welke delen AI-gegenereerd zijn, zodat reviewers extra aandacht kunnen schenken aan die fragmenten. Dit is tijdens het project niet consequent gedaan.
- **Verificatie van beveiligingscode:** AI-tools genereerden cryptografie-code (AES-GCM, hashing) die niet altijd correct was. Afspraak voor een volgend project: alle beveiligingskritische code altijd laten reviewen door een teamlid met een beveiligingsachtergrond, ongeacht de bron.
- **Prompttemplates voor ADR's:** Het opstellen van een vaste prompt-template voor ADR's zou de consistentie tussen de tien beslissingsdocumenten hebben verhoogd.
- **Gebruik van AI voor testscenario's:** We hebben AI voornamelijk voor productiecode ingezet. In een volgend project zouden we eerder AI inzetten om edge-case testscenario's te identificeren.
