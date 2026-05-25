# Implementatietekorten (t.o.v. `assignment.md` / `opdracht.md`)

Dit document vergelijkt de huidige codebase en opgeleverde artefacten met `assignment.md`. Onderstaande punten zijn **ontbrekend, onvolledig of slechts gedeeltelijk ingevuld** en moeten nog worden geïmplementeerd of afgerond voordat de opdracht als compleet kan gelden.

**Laatst beoordeeld:** 2026-05-22 (volledige repo-scan)

> Engelse versie: [`IMPLEMENTATION_GAPS.md`](IMPLEMENTATION_GAPS.md)

---

## Samenvatting

| Gebied | Status |
|--------|--------|
| Kernpipeline (FHIR-intake → planning → RabbitMQ → 4 providers → delivery tracking) | Grotendeels geïmplementeerd |
| Timing patiëntherinneringen (24u / 1u, annuleren/wijzigen, verstreken afspraken) | Geïmplementeerd |
| **Inhoud** notificaties (locatie, instructies, lokale tijd) | **Tekorten** |
| Beveiliging & privacy (bewaartermijn, versleuteling, TLS, logs) | **Grote tekorten** |
| Meerdere tijdzones & karaktersets | **Tekorten** |
| Rapportage / facturatiemetadata (zonder PII, lange bewaartermijn) | **Gedeeltelijk** |
| OpenMRS-beheerdersdocumentatie & uitbreidbaarheid naar andere modules | **Tekorten** |
| Op te leveren artefacten (C4 L3, processtroom, testrapport, projectlog) | **Tekorten** |

---

## 1. Functionele eisen

### 1.1 Patiëntnotificaties — berichtinhoud

**Eis:** Notificaties bevatten **datum/tijd**, **locatie** en **voorbereidingsinstructies** van de afspraak.

**Huidige situatie:** `Location` en `Instructions` worden opgeslagen op `AppointmentRecord` en geaccepteerd via FHIR (`patientInstruction`, locatie-extensie), maar **geen enkele provider-adapter verwerkt ze in uitgaande berichten**. Alle vier adapters formatteren alleen naam, UTC-datum/tijd en status.

**Bestanden:** `src/NotificationModule.Consumer/Adapters/SwiftSendProvider.cs`, `LegacyLinkProvider.cs`, `SecurePostProvider.cs`, `AsyncFlowProvider.cs`

**Te implementeren:**

- Gedeelde berichtformatter (bijv. `NotificationMessageBuilder`) voor alle providers.
- `Location` en `Instructions` opnemen wanneer aanwezig.
- `StartDateTime` formatteren in de **tijdzone van de organisatie** (zie §2.2), niet hardcoded `UTC` in SMS/e-mailtekst.

---

### 1.2 Rapportage & facturatie

**Eis:** Bijhouden of notificaties succesvol zijn verzonden, per organisatie en provider, ter ondersteuning van factuurcontrole.

**Huidige situatie:** `notification_deliveries` slaat provider, status, tijdstempels en fouttekst op. Grafana-panelen en `DASHBOARD_DATABASE.md` ondersteunen operationele/facturatiequeries. Dit is **gedeeltelijk** voldoende voor interne rapportage.

**Tekorten:**

| Tekort | Toelichting |
|--------|-------------|
| PII in facturatiemetadata | `NotificationDeliveryRecord` koppelt aan `AppointmentId` / `ScheduledNotificationId`; joins kunnen patiëntgegevens blootleggen. De opdracht vereist metadata **zonder direct identificeerbare** patiënt-/afspraakgegevens, met voldoende info voor providerfacturatie. |
| Geen export/rapport-API | Geen REST-endpoint of gedocumenteerde export (CSV/JSON) voor factuurafstemming. |
| Provider message-IDs | Geen opslag van externe provider message/tracking-IDs (nuttig bij factuurgeschillen), behalve impliciet in logs voor AsyncFlow. |

**Te implementeren:**

- Facturatiegerichte tabel of view: organisatiesleutel, provider, reminder-type, afleverstatus, tijdstempels, opaque correlatie-id (geen naam/telefoon/e-mail).
- Optioneel: `GET /api/reports/deliveries?organization=…&from=…&to=…` voor beheerders.
- Facturatiequeries documenteren in beheerdersdocumentatie.

---

### 1.3 Providerkeuze (organisatieconfiguratie)

**Eis:** Elke OpenMRS-organisatie kiest een ondersteunde messaging provider.

**Huidige situatie:** `organizations.PreferredProvider` en `FallbackProviders` bestaan; de scheduler zet `TargetProvider`; de consumer publiceert bij falen opnieuw naar de fallback-keten. Standaardwaarden komen alleen uit config/seed.

**Tekorten:**

- Geen API of beheerflow om preferred/fallback providers **per organisatie in te stellen of te wijzigen**.
- Geen validatie dat de gekozen provider **versleutelde credentials** heeft vóór het plannen van verzending.
- Geen UI/documentatie voor operators om org-abonnementen te registreren (behalve env-seed en SQL).

**Te implementeren:**

- Beheer-endpoint(s) of gedocumenteerd migratie-/SQL-playbook voor providerbeleid per org.
- Readiness-check: waarschuwen of blokkeren bij publiceren als secrets van de preferred provider ontbreken.

---

## 2. Niet-functionele eisen

### 2.1 Onafhankelijkheid & integratie

**Eis:** Zelfstandige module; integratie gedocumenteerd voor technische OpenMRS-beheerders; beveiligd volgens best practices.

**Huidige situatie:** Zelfstandige producer/consumer, Docker Compose, FHIR-endpoint (`FHIR_ENDPOINT.md`), API-key-authenticatie, ADR over push/webhook-integratie (`docs/madr/0004-integratiemethode.md`).

**Tekorten:**

| Tekort | Toelichting |
|--------|-------------|
| OpenMRS 2.7.x integratiegids | Geen aparte doc met **stap-voor-stap** OpenMRS-setup (webhook/event-module, FHIR-payload mapping, API-keys, netwerk/TLS). `FHIR_ENDPOINT.md` beschrijft de API, niet de OpenMRS-kant. |
| Webhook-implementatie | ADR kiest push/webhooks; **geen voorbeeld** OpenMRS-module, Groovy-regel of Bahmni-hook die de producer aanroept. |
| Security hardening-gids | Geen beheerdersdoc voor key-rotatie, least-privilege API-keys, productie-TLS-terminatie en secret handling. |

**Te implementeren:**

- `docs/OPENMRS_INTEGRATIE.md` (of vergelijkbaar): OpenMRS 2.7+ → FHIR POST-flow, authenticatie, retry/idempotentie, foutafhandeling.
- Optioneel: minimaal referentie-webhook of OpenMRS-configuratievoorbeelden.

---

### 2.2 Tijdzones

**Eis:** Meerdere tijdzones; geplande verzendtijden en notificatie-inhoud respecteren de lokale tijdzone van de organisatie.

**Huidige situatie:** `OrganizationRecord.TimeZone` wordt opgeslagen (standaard `UTC` uit config). Planning gebruikt **alleen UTC** (`NormalizeToUtc`, `DateTimeOffset.UtcNow`). Uitgaande berichten tonen expliciet **UTC**.

**Tekorten:**

- `ScheduledSendAt` wordt niet berekend in lokale org-tijd (bijv. „24u vóór 14:30 Europe/Amsterdam”).
- Geen gebruik van `TimeZoneInfo` / NodaTime bij planning of berichttekst.

**Te implementeren:**

- Org-tijdzone oplossen bij intake en planning.
- `start` omzetten naar org-TZ voor reminder-offsets; intern UTC opslaan/verzenden mag, maar **lokale tijd tonen** in notificaties.

---

### 2.3 Karaktersets

**Eis:** Berichten verwerken in **meerdere karaktersets**.

**Huidige situatie:** Overal UTF-8 (JSON, XML `encoding="utf-8"`, RabbitMQ-body, logs).

**Te implementeren:**

- UTF-8 documenteren als ondersteunde charset voor FHIR JSON en provider-API's, **of**
- Expliciete charset-onderhandeling/decodering voor legacy-kanalen (bijv. LegacyLink XML) als de opdracht meer dan UTF-8 vereist.

---

### 2.4 Uitbreidbaarheid (andere OpenMRS-modules)

**Eis:** Ontwerp moet integratie van andere modules (bijv. labresultaten) mogelijk maken.

**Huidige situatie:** Pipeline is **afspraak-specifiek** (`AppointmentMessage`, alleen FHIR `Appointment`). Provider-interface is uitbreidbaar voor **nieuwe providers**, niet voor nieuwe **notificatietypes**.

**Te implementeren:**

- Extensiepatroon documenteren (nieuwe FHIR-resource / eventtype → nieuwe handler) in ADR of `docs/UITBREIDBAARHEID.md`.
- Optioneel: generiek `INotificationEvent` + routing key per eventtype, met afspraak als eerste implementatie.

---

### 2.5 Beveiliging & privacy

| Eis | Status | Te implementeren |
|-----|--------|------------------|
| Provider-credentials niet in code/config | Voldaan bij runtime (versleutelde DB + env master key) | `env.example` duidelijk als dev-only markeren |
| AES-256 at rest voor gevoelige data | **Gedeeltelijk** — alleen `provider_secrets` versleuteld | Patiënt/contactvelden en `RawSourcePayload` versleutelen/tokenizen at rest, of minimale retentie + snel verwijderen |
| TLS 1.3 in transit | **Niet afgedwongen** — platte `HttpClient` naar FakeComWorld; geen Kestrel-TLS in compose | TLS bij reverse proxy **en** `Tls13` afdwingen (of productie-deploy met TLS 1.3 documenteren) |
| Geen gevoelige data in logs | **Tekort** — o.a. `FhirAppointmentController` logt `PatientName` | Gestructureerd loggen met redactie; nooit telefoon, e-mail, berichttekst of ruwe FHIR-payload loggen |
| Patiënt-/communicatiegegevens binnen **14 dagen** verwijderen | **Niet geïmplementeerd** | Achtergrondtaak: PII en payloads uit `appointments` wissen/redacteren N dagen na laatste aflevering of einde afspraak |
| Facturatiemetadata **1 jaar** bewaren, zonder PII | **Niet geïmplementeerd** | Archiveren/denormaliseren zonder FK naar PII-tabellen; geplande opschoning operationele data |
| Berichtinhoud versleuteld at rest | **Niet geïmplementeerd** | Afstemmen op 14-dagen verwijdering of field-level encryption |

---

### 2.6 Standaarden & betrouwbaarheid (HL7 / FHIR)

| Onderwerp | Status | Tekort / actie |
|-----------|--------|----------------|
| FHIR-intake + validatie | Geïmplementeerd (`FhirAppointmentValidator`, `OperationOutcome` ACK) | — |
| HTTP-status bij intake | — | Opgelost: FHIR `201`/`200` (ADR 0010); legacy JSON `202` |
| Delivery ACK naar OpenMRS | ADR expliciet **geen** HL7/FHIR delivery ACK | Bij eis van beoordelaars: webhook/callback of DocumentReference-status — anders als geaccepteerde afwijking documenteren |
| Retry / fallback | Provider HTTP-retries (3x), scheduler publish-retry, provider fallback republish | Eén plek documenteren (`docs/BETROUWBAARHEID.md`); **RabbitMQ DLQ** of requeue — nu `BasicNack(..., requeue: false)` **verwijdert** foute berichten |
| OpenMRS-downtime | Niet afgehandeld aan producer-kant behalve client-retry | Idempotente intake documenteren; optionele queue aan OpenMRS-kant |
| Observability | OpenTelemetry, Prometheus, Loki, Jaeger, Grafana-dashboards | Voldaan |
| Real-time beheerdersdashboard | Grafana-dashboards geprovisioneerd | Voldaan voor monitoring; toegangsbeheer voor „OpenMRS-beheerders” verduidelijken |

---

## 3. Op te leveren artefacten (documentatie)

| Op te leveren | Status | Actie |
|---------------|--------|-------|
| Integratiedocumentatie voor beheerders | Gedeeltelijk (`FHIR_ENDPOINT.md`, `README.md`) | Volledige OpenMRS-beheerdersgids (zie §2.1) |
| Docker + voorbeeldrequest | Voldaan (`docker-compose.yml`, `README.md`, curl) | — |
| ADR-log | Voldaan (`docs/madr/`) | Kapotte links in `docs/madr/README.md` repareren (verkeerde bestandsnamen) |
| C4 niveau 1 & 2 | Voldaan (`docs/c4/c1_v2.svg`, `c2_v2.svg`, `expl.md`) | — |
| **C4 niveau 3** (componenten) | **Ontbreekt** | `c3`-diagram toevoegen (Producer API, Scheduler, Dispatcher, Adapters, DB, enz.) |
| **Processtroom** (data door het systeem) | **Ontbreekt** | Sequence- of flowdiagram: OpenMRS → Producer → DB → Scheduler → RabbitMQ → Consumer → Provider → delivery record |
| **Testrapport** (betrouwbaarheid & uitbreidbaarheid) | **Ontbreekt** | Formeel rapport: scope, resultaten, coverage, bewijs uitbreidbaarheid (bijv. vijfde provider) |
| **Projectuitvoeringslog** (IDE's, AI-gebruik, commits per lid) | **Ontbreekt** | `docs/PROJECT_LOG.md` met teamproces-bewijs |

---

## 4. Testtekorten

**Huidige situatie:** Unittests in `tests/NotificationModule.Tests/` (intake, FHIR-mapping, secrets, delivery tracking, queue mapping, error classifier). CI bouwt Docker-images en draait `dotnet test`.

**Ontbreekt voor opdrachtoplevering:**

- Integratie/smoke-tests vastgelegd in een **testrapport** (niet alleen `scripts/smoke-test.sh`).
- Tests die bewijzen dat de notificatie-**body** locatie/instructies bevat (na implementatie).
- Tests voor **retentie**-taak en **tijdzone**-planning.
- Uitbreidbaarheidstest: stub van vijfde provider registreren en dispatch verifiëren.

---

## 5. Voorgestelde implementatieprioriteit

1. **Hoog** — Berichtinhoud: locatie, instructies, lokale tijdzone in tekst.
2. **Hoog** — 14-dagen PII-verwijdering + 1-jaar facturatiemetadata zonder PII.
3. **Hoog** — OpenMRS-beheerdersdocumentatie voor integratie.
4. **Middel** — Tijdzone-bewuste planning (niet alleen weergave).
5. **Middel** — Log-redactie; TLS 1.3-productiestory.
6. **Middel** — C4 L3 + processtroomdiagrammen.
7. **Middel** — RabbitMQ DLQ / afhandeling mislukte berichten (doc + code).
8. **Lager** — Rapport-API, org provider-beheer-API, charset-beleidsdoc, testrapport, projectlog.

---

## 6. Reeds geïmplementeerd (referentie)

Gebruik deze checklist bij voortgang; deze punten staan **niet** als tekort in de secties hierboven.

- [x] Zelfstandige producer + consumer + RabbitMQ + PostgreSQL
- [x] FHIR R4 `Appointment`-intake met validatie en `OperationOutcome` ACK
- [x] Herinneringen 24u en 1u vóór afspraak; catch-up planning; overslaan als afspraak gestart is
- [x] Annuleren/wijzigen: pending notificaties geannuleerd of herbouwd bij update
- [x] Vier providers: SwiftSend, LegacyLink, AsyncFlow, SecurePost
- [x] Per-organisatie versleutelde provider-secrets (AES-256-GCM)
- [x] API-key-authenticatie voor intake
- [x] Delivery tracking in `notification_deliveries`
- [x] Provider fallback-keten bij dispatch-falen
- [x] HTTP-retry bij provider-aanroepen; scheduler-retry bij publish-falen
- [x] OpenTelemetry metrics/traces/logs + Grafana/Prometheus/Loki/Jaeger-stack
- [x] Docker Compose, README, smoke-scripts, GitHub Actions CI
- [x] ADR's en FMEA; C4 context- + containerdiagrammen

---

## Gerelateerde bestanden

| Onderwerp | Locatie |
|-----------|---------|
| Opdrachtspecificatie | `assignment.md`, `opdracht.md` |
| FHIR API | `FHIR_ENDPOINT.md` |
| Verouderde JSON API | `APPOINTMENT_ENDPOINT.md` |
| Dashboard / SQL | `DASHBOARD_DATABASE.md` |
| Architectuur | `docs/c4/expl.md`, `docs/madr/` |
| Engelse versie van dit document | `IMPLEMENTATION_GAPS.md` |
