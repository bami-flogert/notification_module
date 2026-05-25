# Implementation gaps (vs `assignment.md`)

This document compares the current codebase and deliverables against `assignment.md`. Items listed here are **missing, incomplete, or only partially met** and should be implemented or completed before the assignment is considered done.

**Last reviewed:** 2026-05-22 (full repo scan)

> Dutch version: [`IMPLEMENTATIE_TEKORTEN.md`](IMPLEMENTATIE_TEKORTEN.md)

---

## Summary

| Area | Status |
|------|--------|
| Core pipeline (FHIR intake → schedule → RabbitMQ → 4 providers → delivery tracking) | Largely implemented |
| Patient reminder timing (24h / 1h, cancel/reschedule, past appointments) | Implemented |
| Notification **content** (location, instructions, local time) | **Gaps** |
| Security & privacy (retention, encryption scope, TLS, log safety) | **Major gaps** |
| Multi-timezone & character sets | **Gaps** |
| Reporting / billing metadata (PII-free, long retention) | **Partial** |
| OpenMRS admin integration guide & extensibility for other modules | **Gaps** |
| Deliverables (C4 L3, process flow, test report, project log) | **Gaps** |

---

## 1. Functional requirements

### 1.1 Patient notifications — message content

**Requirement:** Notifications must include appointment **date/time**, **location**, and **preparation instructions**.

**Current state:** `Location` and `Instructions` are stored on `AppointmentRecord` and accepted via FHIR (`patientInstruction`, location extension), but **no provider adapter includes them in outbound messages**. All four adapters only format name, UTC date/time, and status.

**Files:** `src/NotificationModule.Consumer/Adapters/SwiftSendProvider.cs`, `LegacyLinkProvider.cs`, `SecurePostProvider.cs`, `AsyncFlowProvider.cs`

**To implement:**

- Shared message formatter (e.g. `NotificationMessageBuilder`) used by all providers.
- Include `Location` and `Instructions` when present.
- Format `StartDateTime` in the **organization time zone** (see §2.2), not hard-coded `UTC` in SMS/email text.

---

### 1.2 Reporting & billing

**Requirement:** Record whether notifications were sent, per organization and provider, to support invoice verification.

**Current state:** `notification_deliveries` stores provider, status, timestamps, and error text. Grafana panels and `DASHBOARD_DATABASE.md` support operational/billing queries. This is **partially** sufficient for internal reporting.

**Gaps:**

| Gap | Detail |
|-----|--------|
| PII in billing metadata | `NotificationDeliveryRecord` links to `AppointmentId` / `ScheduledNotificationId`; joined queries can expose patient fields. Assignment requires metadata **without directly identifiable** patient/appointment data while still allowing provider billing checks. |
| No export/report API | No REST endpoint or documented export (CSV/JSON) for billing reconciliation. |
| Provider message IDs | No storage of external provider message/tracking IDs (useful for disputing invoices), except implicitly via logs for AsyncFlow. |

**To implement:**

- Billing-oriented table or view: organization key, provider, reminder type, delivery status, timestamps, opaque correlation id (no name/phone/email).
- Optional: `GET /api/reports/deliveries?organization=…&from=…&to=…` for administrators.
- Document billing queries in admin documentation.

---

### 1.3 Provider selection (organization configuration)

**Requirement:** Each OpenMRS organization chooses a supported messaging provider.

**Current state:** `organizations.PreferredProvider` and `FallbackProviders` exist; scheduler sets `TargetProvider`; consumer republishes to fallback chain on failure. Defaults come from config/seed only.

**Gaps:**

- No API or admin flow to **set or update** preferred/fallback providers per organization.
- No validation that the chosen provider has **encrypted credentials** configured before scheduling sends.
- No UI/documentation for operators to register org subscriptions (beyond env seed and SQL).

**To implement:**

- Management endpoint(s) or documented migration/SQL playbook for org provider policy.
- Readiness check: warn or block publish if preferred provider secrets are missing.

---

## 2. Non-functional requirements

### 2.1 Independence & integration

**Requirement:** Standalone module; integration documented for technical OpenMRS administrators; secured per best practices.

**Current state:** Standalone producer/consumer, Docker Compose, FHIR endpoint (`FHIR_ENDPOINT.md`), API key auth, ADR on push/webhook integration (`docs/madr/0004-integratiemethode.md`).

**Gaps:**

| Gap | Detail |
|-----|--------|
| OpenMRS 2.7.x integration guide | No dedicated doc with **step-by-step** OpenMRS setup (webhook/event module, FHIR payload mapping, API keys, network/TLS). `FHIR_ENDPOINT.md` covers the API, not the OpenMRS side. |
| Webhook implementation | ADR chooses push/webhooks; **no sample OpenMRS module, Groovy rule, or Bahmni hook** showing how to call the producer. |
| Security hardening guide | Missing administrator doc for key rotation, least-privilege API keys, production TLS termination, and secret handling. |

**To implement:**

- `docs/OPENMRS_INTEGRATION.md` (or similar): OpenMRS 2.7+ → FHIR POST flow, authentication, retry/idempotency, failure handling.
- Optional: minimal reference webhook sender or OpenMRS configuration snippets.

---

### 2.2 Time zones

**Requirement:** Multiple time zones; scheduled send times and notification content respect the organization’s local time zone.

**Current state:** `OrganizationRecord.TimeZone` is stored (default `UTC` from config). Scheduling uses **UTC only** (`NormalizeToUtc`, `DateTimeOffset.UtcNow`). Outbound messages display **UTC** explicitly.

**Gaps:**

- Reminder `ScheduledSendAt` is not computed in org local time (e.g. “24h before 14:30 Europe/Amsterdam”).
- No use of `TimeZoneInfo` / NodaTime when building schedules or message text.

**To implement:**

- Resolve org time zone when ingesting and scheduling.
- Convert `start` to org TZ for reminder offsets; store/send UTC internally if desired, but **display local time** in notifications.

---

### 2.3 Character sets

**Requirement:** Process messages in **multiple character sets**.

**Current state:** UTF-8 throughout (JSON, XML `encoding="utf-8"`, RabbitMQ body, logs).

**To implement:**

- Document UTF-8 as the supported charset for FHIR JSON and provider APIs, **or**
- Add explicit charset negotiation/decoding for legacy channels (e.g. LegacyLink XML) if the assignment requires more than UTF-8.

---

### 2.4 Extensibility (other OpenMRS modules)

**Requirement:** Design allows other modules (e.g. lab results) to integrate.

**Current state:** Pipeline is **appointment-specific** (`AppointmentMessage`, FHIR `Appointment` only). Provider interface is extensible for **new providers**, not new **notification types**.

**To implement:**

- Document extension pattern (new FHIR resource / event type → new handler) in ADR or `docs/EXTENSIBILITY.md`.
- Optional: generic `INotificationEvent` + routing key per event type, with appointment as first implementation.

---

### 2.5 Security & privacy

| Requirement | Status | What to implement |
|-------------|--------|-------------------|
| Provider credentials not in code/config | Met for runtime (encrypted DB + env master key) | Keep `env.example` clearly marked dev-only |
| AES-256 at rest for sensitive data | **Partial** — only `provider_secrets` encrypted | Encrypt or tokenize patient/contact fields and `RawSourcePayload` at rest, or justify minimal retention and delete quickly |
| TLS 1.3 in transit | **Not enforced** — plain `HttpClient` to FakeComWorld; no Kestrel TLS config in compose | Terminate TLS at reverse proxy **and** enforce `Tls13` (or document production deployment with TLS 1.3) |
| No sensitive data in logs | **Gap** — e.g. `FhirAppointmentController` logs `PatientName` | Structured logging with redaction; never log phone, email, message body, or raw FHIR payload |
| Delete patient/communication data within **14 days** | **Not implemented** | Background job: purge/redact `appointments` PII and message payloads N days after last delivery or appointment end |
| Retain billing metadata up to **1 year**, PII-free | **Not implemented** | Archive/denormalize billing rows without FK to PII tables; scheduled purge of old operational data |
| Message content encrypted at rest | **Not implemented** | Align with 14-day deletion or field-level encryption |

---

### 2.6 Standards & reliability (HL7 / FHIR)

| Topic | Status | Gap / action |
|-------|--------|----------------|
| FHIR intake + validation | Implemented (`FhirAppointmentValidator`, `OperationOutcome` ACK) | — |
| Intake HTTP status | — | Resolved: FHIR uses `201`/`200` (ADR 0010); legacy JSON uses `202` |
| Delivery ACK to OpenMRS | ADR explicitly **no** HL7/FHIR delivery ACK | If assessors require delivery ACK: webhook/callback or DocumentReference status — otherwise document as accepted deviation |
| Retry / fallback | Provider HTTP retries (3x), scheduler publish retry, provider fallback republish | Document in one place (`docs/RELIABILITY.md`); add **RabbitMQ DLQ** or requeue strategy — today `BasicNack(..., requeue: false)` **drops** poison messages |
| OpenMRS downtime | Not handled on producer side beyond client retry | Document idempotent intake; optional queue on OpenMRS side |
| Observability | OpenTelemetry, Prometheus, Loki, Jaeger, Grafana dashboards | Met |
| Real-time admin dashboard | Grafana dashboards provisioned | Met for monitoring; clarify access control for “OpenMRS administrators” |

---

## 3. Deliverables (documentation & artifacts)

| Deliverable | Status | Action |
|-------------|--------|--------|
| Administrator integration documentation | Partial (`FHIR_ENDPOINT.md`, `README.md`) | Full OpenMRS admin guide (see §2.1) |
| Docker runnable + sample request | Met (`docker-compose.yml`, `README.md`, curl) | — |
| ADR log | Met (`docs/madr/`) | Fix broken links in `docs/madr/README.md` (points to wrong filenames) |
| C4 Level 1 & 2 | Met (`docs/c4/c1_v2.svg`, `c2_v2.svg`, `expl.md`) | — |
| **C4 Level 3** (components) | **Missing** | Add `c3` diagram (Producer API, Scheduler, Dispatcher, Adapters, DB, etc.) |
| **Process flow** (data through system) | **Missing** | Sequence or flow diagram: OpenMRS → Producer → DB → Scheduler → RabbitMQ → Consumer → Provider → delivery record |
| **Test report** (reliability & extensibility) | **Missing** | Formal report: scope, results, coverage, extensibility proof (e.g. adding a fifth provider) |
| **Project execution log** (IDEs, AI usage, commits per member) | **Missing** | `docs/PROJECT_LOG.md` with team process evidence |

---

## 4. Testing gaps

**Current state:** Unit tests in `tests/NotificationModule.Tests/` (ingestion, FHIR mapping, secrets, delivery tracking, queue mapping, error classifier). CI builds Docker images and runs `dotnet test`.

**Missing for assignment deliverable:**

- Integration/smoke tests documented as a **test report** (not only `scripts/smoke-test.sh`).
- Tests proving notification **body** contains location/instructions once implemented.
- Tests for **retention** job and **timezone** scheduling.
- Extensibility test: register a stub fifth provider and verify dispatch.

---

## 5. Suggested implementation priority

1. **High** — Notification body: location, instructions, local timezone in text.
2. **High** — 14-day PII purge + 1-year PII-free billing metadata model.
3. **High** — OpenMRS administrator integration documentation.
4. **Medium** — Timezone-aware scheduling (not only display).
5. **Medium** — Log redaction; TLS 1.3 production story.
6. **Medium** — C4 L3 + process flow diagrams.
7. **Medium** — RabbitMQ DLQ / failed-message handling documentation and implementation.
8. **Lower** — Report API, org provider management API, charset policy doc, test report, project log.

---

## 6. Already implemented (reference)

Use this checklist when marking progress; these items are **not** listed as gaps above.

- [x] Standalone producer + consumer + RabbitMQ + PostgreSQL
- [x] FHIR R4 `Appointment` intake with validation and `OperationOutcome` ACK
- [x] Reminders 24h and 1h before appointment; catch-up scheduling; skip if appointment started
- [x] Cancel/reschedule: pending notifications cancelled or rebuilt on update
- [x] Four providers: SwiftSend, LegacyLink, AsyncFlow, SecurePost
- [x] Per-organization encrypted provider secrets (AES-256-GCM)
- [x] API key authentication for intake
- [x] Delivery tracking in `notification_deliveries`
- [x] Provider fallback chain on dispatch failure
- [x] HTTP retry on provider calls; scheduler retry on publish failure
- [x] OpenTelemetry metrics/traces/logs + Grafana/Prometheus/Loki/Jaeger stack
- [x] Docker Compose, README, smoke scripts, GitHub Actions CI
- [x] ADRs and FMEA; C4 context + container diagrams

---

## Related files

| Topic | Location |
|-------|----------|
| Assignment spec | `assignment.md` |
| FHIR API | `FHIR_ENDPOINT.md` |
| Legacy JSON API | `APPOINTMENT_ENDPOINT.md` |
| Dashboard / SQL | `DASHBOARD_DATABASE.md` |
| Architecture | `docs/c4/expl.md`, `docs/madr/` |
| Dutch version of this document | `IMPLEMENTATIE_TEKORTEN.md` |
