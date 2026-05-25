# GitHub issues backlog & sprint plan

Use this file to create issues in GitHub (`gh issue create` or the web UI). Each issue has a **fixed title**, **priority label**, **acceptance criteria** (Definition of Done), and **estimate** (story points, SP).

**Priority labels:** `P0-critical` → `P1-high` → `P2-medium` → `P3-low`

**Sprint assumption:** 1 sprint = **10 working days**, team = **4 developers**, velocity ≈ **80 SP**. Adjust dates when you create the GitHub Milestone.

---

## Issue list (priority order)


| #   | Priority | Title                                                                 | SP  | Depends on | Status        |
| --- | -------- | --------------------------------------------------------------------- | --- | ---------- | ------------- |
| 1   | P0       | Shared notification message body (location, instructions, local time) | 5   | —          | **Done**      |
| 2   | P0       | Timezone-aware reminder scheduling                                    | 8   | —          | **Done**      |
| 3   | P0       | 14-day PII purge background job                                       | 8   | —          | **Done**      |
| 4   | P0       | PII-free billing ledger (1-year retention)                            | 8   | 3          | **Done**      |
| 5   | P0       | Log redaction: no PII in application logs                             | 3   | —          | **Done**      |
| 6   | P0       | OpenMRS 2.7+ integration guide for administrators                     | 5   | —          | Open          |
| 7   | P1       | RabbitMQ dead-letter queue and failed-message policy                  | 5   | —          | **Done**      |
| 8   | P1       | Organization provider policy API and publish readiness check          | 5   | —          | **Done**      |
| 9   | P1       | Billing deliveries report API (admin, API key)                        | 5   | 4          | **Done**      |
| 10  | P1       | Production TLS 1.3 deployment and security hardening guide            | 5   | 6          | Open          |
| 11  | P2       | C4 Level 3 component diagram                                          | 3   | —          | Open          |
| 12  | P2       | End-to-end process flow diagram                                       | 3   | 11         | Open          |
| 13  | P2       | Reliability and retry documentation                                   | 2   | 7          | Open          |
| 14  | P2       | Fix broken links in `docs/madr/README.md`                             | 1   | —          | **Done**      |
| 15  | P2       | Align ADR 0008 HTTP intake status with implementation                 | 2   | —          | **Done**      |
| 16  | P2       | Extensibility guide for non-appointment notification types            | 3   | —          | **Done**      |
| 17  | P2       | Character set policy documentation (UTF-8)                            | 1   | —          | **Done**      |
| 18  | P2       | Store external provider message/tracking IDs on delivery              | 3   | 4          | **Done**      |
| 19  | P3       | Automated tests for gaps (message, TZ, retention, billing, DLQ)       | 8   | 1–4, 7     | **Done**      |
| 20  | P3       | Formal test report (reliability & extensibility)                      | 3   | 19         | Open          |
| 21  | P3       | Project execution log (IDEs, AI, commits per member)                  | 2   | —          | Open          |
| 22  | P3       | Extensibility proof: register fifth stub provider in tests            | 3   | 16         | **Cancelled** |


**Total: 98 SP** (fits one sprint with 4 devs @ ~20 SP each if work is parallelized; see sprint board below).

---

## GitHub issue bodies (copy-paste)

### Issue #1 — P0: Shared notification message body

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P0-critical`, `functional`, `consumer`

**Description:**

All four messaging providers must send the same appointment reminder content required by the assignment.

**Acceptance criteria:**

- New class `NotificationMessageBuilder` in `NotificationModule.Shared` (or `Consumer`) builds SMS/email body from `AppointmentMessage`.
- Body always includes: patient display name (if present), appointment start in **organization local time** (not the string `UTC`), appointment status.
- If `Location` is non-empty, body includes a line `Location: {value}`.
- If `Instructions` is non-empty, body includes a line `Instructions: {value}`.
- `SwiftSendProvider`, `LegacyLinkProvider`, `SecurePostProvider`, and `AsyncFlowProvider` call `NotificationMessageBuilder` only (no duplicate format strings).
- Unit test `NotificationMessageBuilderTests` asserts all four fields appear when set.

**Delivered in:** `NotificationMessageBuilder`, `OrganizationTimeZone`, four provider adapters, `NotificationSchedulerWorker` (`TimeZone` on publish), `NotificationMessageBuilderTests`; removed SwiftSend message-body log (PII).

**Out of scope:** Timezone scheduling logic (Issue #2).

---

### Issue #2 — P0: Timezone-aware reminder scheduling

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P0-critical`, `producer`

**Description:**

Reminder send times must respect `organizations.TimeZone` (IANA ID, e.g. `Europe/Amsterdam`).

**Acceptance criteria:**

- `AppointmentIngestionService.RebuildPendingNotifications` computes `idealSendAt` using org timezone: “appointment local start minus 24h/1h”, stored as UTC in `ScheduledSendAt`.
- Invalid or missing timezone falls back to `UTC` and logs a warning once per organization.
- Unit tests cover: org `Europe/Amsterdam`, appointment `2026-06-01T14:30` local → `24h` reminder at correct UTC instant.
- `NotificationMessageBuilder` (Issue #1) uses the same timezone resolution for display.

**Delivered in:** `AppointmentIngestionService.NormalizeToUtc`, `OrganizationTimeZone`, warn-once per org key, `AppointmentIngestionServiceTests` (Amsterdam + invalid TZ), scheduler passes `TimeZone` to consumer messages.

**Out of scope:** Changing FHIR `start` parsing rules.

---

### Issue #3 — P0: 14-day PII purge background job

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P0-critical`, `security`, `producer`

**Description:**

Assignment requires deletion of patient-related and communication-related data within 14 days after processing.

**Acceptance criteria:**

- New `DataRetentionWorker` (hosted service) runs daily (configurable `DataRetention:RunIntervalHours`, default `24`).
- Configurable `DataRetention:RetentionDays` default `14`.
- For each `appointments` row where `UpdatedAt` (or last delivery `SentAt`, whichever is later) is older than 14 days: set `PatientName`, `PatientPhone`, `PatientEmail`, `Instructions`, `Location`, `RawSourcePayload` to `NULL`; keep `PatientUuid`, `AppointmentUuid`, `StartDateTime`, `Status`, IDs for correlation.
- After purge, no log statement in the retention job contains cleared field values.
- Unit or integration test: `DataRetentionRulesTests` (UpdatedAt / SentAt cutoff rules).

**Delivered in:** `DataRetentionWorker`, `DataRetentionRules`, migration `PiiPurgedAt`, `DataRetentionRulesTests`.

**Out of scope:** Deleting `notification_deliveries` billing rows (Issue #4).

---

### Issue #4 — P0: PII-free billing ledger (1-year retention)

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P0-critical`, `billing`, `database`

**Description:**

Billing metadata must be kept up to 1 year without directly identifiable patient data.

**Acceptance criteria:**

- New table `billing_delivery_events` (see schema note below).
- On each delivery attempt (success or failure), consumer writes one row to `billing_delivery_events` without patient name, phone, email, or appointment UUID.
- EF migration applied; documented in `DASHBOARD_DATABASE.md`.
- Retention job (Issue #3) does **not** delete `billing_delivery_events` younger than 365 days.
- Job deletes (or archives) `billing_delivery_events` older than 365 days.

**Schema note:** Implementation uses `OrganizationId` (join `organizations` for `Key`), single `OccurredAt` + `Status` instead of separate `SentAt`/`FailedAt`, and `CorrelationId` (opaque GUID). `ProviderMessageId` added in **#18**. Report API **#9** maps these fields.

**Delivered in:** `BillingDeliveryEventRecord`, migration `20260524120000_AddDataRetentionAndBillingLedger`, `DeliveryTrackingService`, `DASHBOARD_DATABASE.md`, `DeliveryTrackingServiceTests.RecordAsync_writes_billing_event_without_pii`.

**Depends on:** Issue #3 (same retention worker can host both jobs).

---

### Issue #5 — P0: Log redaction — no PII in application logs

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P0-critical`, `security`

**Acceptance criteria:**

- `FhirAppointmentController` does not log `PatientName`, phone, email, or raw FHIR body.
- Provider adapters log only: `AppointmentUuid`, `OrganizationKey`, `ScheduledNotificationId`, provider name, HTTP status.
- Add `docs/LOGGING.md` listing allowed log fields and forbidden fields.
- Grep CI step or unit test: fail build if `LogInformation`/`LogError` in Producer/Consumer contains substring `PatientName` or `PatientPhone` in source (simple analyzer test acceptable).

**Delivered in:** `FhirAppointmentController`, `ProviderLogging` + four provider adapters, `docs/LOGGING.md`, `tests/NotificationModule.Tests/Security/LogRedactionTests.cs`.

---

### Issue #6 — P0: OpenMRS 2.7+ integration guide

**Labels:** `P0-critical`, `documentation`

**Acceptance criteria:**

- New file `docs/OPENMRS_INTEGRATION.md` (English; optional Dutch summary paragraph at top).
- Sections (all required): Prerequisites (OpenMRS 2.7.x), Network & TLS, API key provisioning, FHIR `Appointment` mapping table (OpenMRS field → FHIR element), Example curl, Webhook/event trigger options (REST module / scheduled task), Idempotency (`AppointmentUuid`), Error handling (4xx/5xx + retry), Cancellation/update flow.
- Link from root `README.md` under “Integration”.
- Reviewed: another team member checked steps against running `docker compose` stack.

---

### Issue #7 — P1: RabbitMQ DLQ and failed-message policy

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P1-high`, `reliability`, `consumer`

**Acceptance criteria:**

- Declare DLQ per provider queue: `notifications.{provider}.dlq` bound via dead-letter exchange or explicit publish on final failure.
- Replace `BasicNack(..., requeue: false)` on processing exceptions with: max 3 deliveries → route to DLQ; deserialization errors → DLQ immediately (no requeue).
- Metric `notification_messages_dlq_total` incremented on DLQ publish.
- Section in `docs/RELIABILITY.md` (Issue #13) describes operator recovery (replay from DLQ).

**Delivered in:** `RabbitMqTopology`, `RabbitMqDeadLetterPublisher`, `RabbitMqMessageFailurePolicy`, `NotificationWorker`, `NotificationTelemetry.NotificationMessagesDlq`, `docs/RELIABILITY.md` (DLQ section; full doc completed in #13), `docs/madr/0009-dead-letter-queues.md`, Grafana panel **Messages Dead-Lettered Rate**.

---

### Issue #8 — P1: Organization provider policy API

**Status:** ✅ **Resolved** (2026-05-25)

**Labels:** `P1-high`, `api`, `producer`

**Acceptance criteria:**

- `PUT /api/organizations/{organizationKey}/providers` body: `{ "preferredProvider": "SwiftSend", "fallbackProviders": "LegacyLink,AsyncFlow" }` (API key required).
- Validates provider name is one of: `SwiftSend`, `LegacyLink`, `AsyncFlow`, `SecurePost`.
- `NotificationSchedulerWorker` skips publish (logs warning, leaves `Pending`) if preferred provider has no row in `provider_secrets` for that organization.
- Integration test: org without secrets → notification stays `Pending`.

**Delivered in:** `NotificationProviders`, `OrganizationProviderPolicyService`, `OrganizationsController`, `NotificationSchedulerPublish`, `INotificationMessagePublisher`, `OrganizationProviderPolicyServiceTests`, `NotificationSchedulerReadinessTests`.

---

### Issue #9 — P1: Billing deliveries report API

**Status:** ✅ **Resolved** (2026-05-25)

**Labels:** `P1-high`, `api`, `billing`

**Depends on:** Issue #4

**Acceptance criteria:**

- `GET /api/reports/deliveries?organizationKey={key}&from={iso8601}&to={iso8601}` returns JSON array from `billing_delivery_events` only.
- Response fields: `organizationKey`, `provider`, `reminderType`, `status`, `sentAt`, `failedAt`, `providerMessageId`, `correlationId`.
- No field in response contains patient name, phone, email, or `AppointmentUuid`.
- Protected by same API key filter as appointment intake.
- Documented in `docs/OPENMRS_INTEGRATION.md` or `DASHBOARD_DATABASE.md`.

**Delivered in:** `BillingDeliveryReportItem`, `BillingDeliveriesReportService`, `ReportsController`, `BillingDeliveriesReportServiceTests`; API documented in `[DASHBOARD_DATABASE.md](../DASHBOARD_DATABASE.md)` (Billing deliveries report API section).

---

### Issue #10 — P1: TLS 1.3 production deployment guide

**Labels:** `P1-high`, `security`, `documentation`

**Depends on:** Issue #6 (same security chapter cross-link)

**Acceptance criteria:**

- New file `docs/PRODUCTION_SECURITY.md` covers: reverse proxy TLS 1.3 termination, `ASPNETCORE_URLS` vs HTTPS, RabbitMQ TLS, PostgreSQL TLS, secret rotation for `SECRETS_MASTER_KEY_BASE64` and API keys.
- `docker-compose.prod.yml` **or** documented `nginx` sample config enforces TLS 1.3 for producer/consumer external ports.
- `env.example` comments: no production secrets; `SecretsSeed__`* dev-only.
- README links to `PRODUCTION_SECURITY.md`.

---

### Issue #11 — P2: C4 Level 3 component diagram

**Labels:** `P2-medium`, `documentation`, `architecture`

**Acceptance criteria:**

- New `docs/c4/c3_components.svg` (or `.png` exported from Structurizr/draw.io).
- Components inside Producer: `FhirAppointmentController`, `AppointmentIngestionService`, `NotificationSchedulerWorker`, `RabbitMqPublisher`.
- Components inside Consumer: `NotificationWorker`, `NotificationDispatcher`, four adapters, `DeliveryTrackingService`, `ProviderSecretsStore`.
- `docs/c4/expl.md` updated with C3 section and image embed.

---

### Issue #12 — P2: End-to-end process flow diagram

**Labels:** `P2-medium`, `documentation`, `architecture`

**Depends on:** Issue #11 (same doc folder)

**Acceptance criteria:**

- New `docs/c4/process_flow.svg` (sequence or flowchart).
- Steps labeled: OpenMRS → FHIR POST → DB appointments/scheduled_notifications → Scheduler → RabbitMQ → Consumer → Provider HTTP → `notification_deliveries` + `billing_delivery_events`.
- Cancel/update path shown as alternate flow.
- Linked from `docs/c4/expl.md` and `README.md`.

---

### Issue #13 — P2: Reliability and retry documentation

**Labels:** `P2-medium`, `documentation`

**Depends on:** Issue #7

**Acceptance criteria:**

- New `docs/RELIABILITY.md` documents: scheduler retry, provider HTTP retry (3x), provider fallback republish, DLQ behavior, stale `Publishing` requeue, OpenMRS client retry guidance.
- Matches actual code paths (file references per mechanism).

---

### Issue #14 — P2: Fix ADR README links

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P2-medium`, `documentation`

**Acceptance criteria:**

- `docs/madr/README.md` table links resolve to existing files (`0001-intro.md`, `0007-opentelemetry-logs-with-loki.md`, etc.).
- No broken relative links in markdown link check (manual or `markdown-link-check` in CI optional).

**Delivered in:** `[docs/madr/README.md](madr/README.md)` (full ADR index incl. 0009, 0010), `[docs/madr/0001-intro.md](madr/0001-intro.md)` expanded, link to `ADR-Sjabloon.md`.

---

### Issue #15 — P2: Align ADR 0008 with HTTP intake status codes

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P2-medium`, `documentation`, `api`

**Acceptance criteria:**

- Decision recorded: either update ADR 0008 to state `201 Created` / `200 OK` for FHIR intake, **or** change `FhirAppointmentController` to return `202 Accepted` on successful ingest (pick one).
- `FHIR_ENDPOINT.md` and ADR 0008 text match implementation.
- No mention of `202` remains in docs if implementation uses `201`/`200`.

**Decision:** Keep implementation (`201`/`200` on FHIR); ADR [0010](madr/0010-fhir-integratie.md) records FHIR choice and status codes; [0008](madr/0008-delivery-acknowledgements.md) marked **Superseded**. Legacy `POST /api/appointments` still documents `202 Accepted` in `[APPOINTMENT_ENDPOINT.md](../APPOINTMENT_ENDPOINT.md)`.

---

### Issue #16 — P2: Extensibility guide

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P2-medium`, `documentation`

**Acceptance criteria:**

- New `docs/EXTENSIBILITY.md` explains how to add: (1) a fifth messaging provider, (2) a new notification type (e.g. lab result) via new handler + queue + FHIR resource mapping.
- Lists files to touch for each extension path.
- Linked from README.

**Delivered in:** `[docs/EXTENSIBILITY.md](EXTENSIBILITY.md)` (Nederlands, eenvoudige taal), README documentatietabel.

---

### Issue #17 — P2: Character set policy

**Status:** ✅ **Resolved** (2026-05-23)

**Labels:** `P2-medium`, `documentation`

**Acceptance criteria:**

- New section in `docs/EXTENSIBILITY.md` or `docs/OPENMRS_INTEGRATION.md` titled “Character encoding”.
- States: FHIR JSON and all provider payloads use UTF-8; LegacyLink XML declares `utf-8`; unsupported encodings return `400` with `OperationOutcome`.
- If non-UTF-8 request detected in FHIR controller, return explicit error (implement if not present).

**Delivered in:** `FhirRequestEncoding`, `FhirAppointmentController`, `FhirRequestEncodingTests`, sectie “Tekenset” in `EXTENSIBILITY.md`.

---

### Issue #18 — P2: Store provider message/tracking IDs

**Status:** ✅ **Resolved** (2026-05-25)

**Labels:** `P2-medium`, `billing`, `consumer`

**Depends on:** Issue #4

**Acceptance criteria:**

- Provider adapters extract external reference from HTTP response (via `ProviderResponseIds`): SwiftSend JSON `messageId`, SecurePost JSON `trackingId`, LegacyLink XML `MessageReference`, AsyncFlow JSON `trackingId`.
- Persisted on `billing_delivery_events.ProviderMessageId` and `notification_deliveries.ProviderMessageId` (nullable, max 128).
- Migration included; tests use FakeComWorld response fixtures and assert ID stored.

**Delivered in:** `ProviderResponseIds`, four provider adapters (`SendAsync` returns `string?`), `NotificationDispatchResult.ProviderMessageId`, `DeliveryTrackingService.RecordAsync`, migration `20260525120000_AddProviderMessageId`, `ProviderResponseIdsTests`, `DeliveryTrackingServiceTests.RecordAsync_persists_provider_message_id_on_delivery_and_billing`.

**Out of scope:** Billing report API field exposure (**#9**).

---

### Issue #19 — P3: Automated tests for new features

**Status:** ✅ **Resolved** (2026-05-25)

**Labels:** `P3-low`, `testing`

**Depends on:** Issues #1–4, #7

**Acceptance criteria:**

- Tests added: `NotificationMessageBuilderTests`, timezone scheduling tests, retention purge test, billing event write test (no PII), DLQ routing test (Testcontainers RabbitMQ or unit-tested republish helper).
- `dotnet test NotificationModule.sln` green in CI.
- Coverage noted in test report (Issue #20).

**Delivered in:** `NotificationMessageBuilderTests`, `AppointmentIngestionServiceTests` (TZ), `DataRetentionRulesTests`, `DataRetentionPurgeTests`, `DeliveryTrackingServiceTests`, `RabbitMqMessageFailurePolicyTests`, `RabbitMqDeadLetterPublisherTests`, `AppointmentMessageJsonTests`, `LogRedactionTests`; purge logic extracted to `DataRetentionPurge` for worker tests.

---

### Issue #20 — P3: Formal test report

**Labels:** `P3-low`, `documentation`, `deliverable`

**Depends on:** Issue #19

**Acceptance criteria:**

- New `docs/TEST_REPORT.md` with: test scope, environment (Docker), results table (pass/fail), reliability scenarios (provider down, RabbitMQ down), extensibility scenario (fifth provider stub).
- Includes output snippet or CI link from latest green run.
- Signed off by team lead with date.

---

### Issue #21 — P3: Project execution log

**Labels:** `P3-low`, `documentation`, `deliverable`

**Acceptance criteria:**

- New `docs/PROJECT_LOG.md` with: team members, IDEs used, AI tools used (with 2 example prompts redacted if needed), table of commits per member (GitHub username + link to commit range or PR list).
- Generation command documented: `git shortlog -sn --since="2026-01-01"`.

---

### Issue #22 — P3: Extensibility proof — fifth provider stub

**Status:** ❌ **Cancelled** — not required; extensibility is already covered by `[docs/EXTENSIBILITY.md](EXTENSIBILITY.md)` and Issue #16.

**Labels:** `P3-low`, `testing`, `extensibility`

**Depends on:** Issue #16

**Acceptance criteria:**

- ~~Test project contains `StubProvider : INotificationProvider` registered only in test DI.~~
- ~~Test proves `NotificationDispatcher` resolves and calls stub without modifying production `Program.cs`.~~
- ~~Referenced in `docs/TEST_REPORT.md` extensibility section.~~

**Reason:** Fifth-provider stub test deferred; documentation and existing provider adapter tests are sufficient for assignment scope.

---

## Sprint plan: “Assignment completion” (10 working days)

**Milestone name:** `Sprint 1 — Assignment completion`  
**Dates (example):** Mon 2026-05-26 → Fri 2026-06-06 (adjust to your calendar)  
**Team:** Dev A, Dev B, Dev C, Dev D (+ optional Tech writer = Dev C on docs days)

### Parallel workstreams


| Stream                  | Owner              | Issues                                       |
| ----------------------- | ------------------ | -------------------------------------------- |
| **Core product**        | Dev A              | ~~#1~~ ✅ → ~~#2~~ ✅ → ~~#8~~ ✅               |
| **Security & data**     | Dev B              | ~~#5~~ ✅ → ~~#3~~ ✅ → ~~#4~~ ✅ → ~~#18~~ ✅   |
| **Reliability & API**   | Dev C              | ~~#7~~ ✅ → ~~#9~~ ✅ → #10                    |
| **Docs & architecture** | Dev D              | #6 → #11 → #12 → #13 → #14 → #15 → #16 → #17 |
| **QA & deliverables**   | Dev A + D (week 2) | #19 → #20 → #21 → #22                        |


### Sprint goal (single sentence)

At sprint end, a reviewer can: POST a FHIR appointment, receive a reminder SMS text with **local time + location + instructions**, see **PII-free billing rows** for 1 year, confirm **PII purged after 14 days**, read **OpenMRS integration + security + architecture docs**, and read **TEST_REPORT.md** with green CI.

### Definition of Done (sprint level)

- All 22 issues closed in GitHub.
- `dotnet test` and GitHub Actions CI green on `main`.
- `docker compose --env-file env.example up --build` succeeds; `scripts/smoke-test.sh` passes.
- No P0/P1 issue open.

### Risk buffer

- **D5** and **D9** are explicit buffer days; if #2 or #4 slip, drop #22 to follow-up only if #19 and #20 already prove extensibility via documentation.
- If team has **3 developers**, extend sprint to **15 working days** (same issues, +50% calendar time).

---

## Quick create script (GitHub CLI)

Run from repo root (requires `gh auth login`):

```bash
# Example — repeat for each issue with --title, --body-file, --label
gh label create "P0-critical" --color "b60205" 2>/dev/null || true
gh label create "P1-high" --color "d93f0b" 2>/dev/null || true
gh label create "P2-medium" --color "fbca04" 2>/dev/null || true
gh label create "P3-low" --color "0e8a16" 2>/dev/null || true

gh milestone create "Sprint 1 — Assignment completion" --due-date "2026-06-06"
```

Create issues manually from sections above, or split this file into `docs/github-issues/issue-01.md` … `issue-22.md` and script bulk creation.

---

## Priority summary (for GitHub Project board)

1. **P0 (must ship):** ~~#1~~ ✅, ~~#2~~ ✅, ~~#3~~ ✅, ~~#4~~ ✅, ~~#5~~ ✅, #6
2. **P1 (ship in same sprint):** ~~#7~~ ✅, ~~#8~~ ✅, ~~#9~~ ✅, #10
3. **P2 (docs + hardening):** #11–#17, ~~#18~~ ✅
4. **P3 (evidence of done):** #19–#22

Order in the backlog view: sort by issue number = priority order.