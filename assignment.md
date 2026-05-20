# OpenMRS Communication Module

## Context

Messaging platforms differ significantly by region; for example, in China different systems are used (such as Baidu services), while in Europe platforms like WhatsApp are common. Due to the lack of standardization, each platform requires a separate integration, since WhatsApp and Signal, for instance, expose different APIs. The client aims to offer the communication module as a standalone product to OpenMRS organizations worldwide. By positioning it as a SaaS solution, organizations can subscribe to the service without managing their own infrastructure, simplifying adoption.

## Objective

Design and implement a Software-as-a-Service communication module capable of sending notifications for OpenMRS organizations via external messaging providers. Use the fictional messaging providers: SwiftSend, LegacyLink, AsyncFlow, and SecurePost. The module must comply with HL7 standards according to the FHIR specification. The communication module must be configurable and extensible, allowing OpenMRS organizations to integrate their own subscriptions and services. In addition, the design must be future-proof so that new communication providers can be added easily.

## Functional Requirements

### 1. Patient Notifications

As a hospital patient, I want to receive a message on my phone with the details of my appointment (time, location, and any preparations), so that I can prepare properly for my hospital visit and arrive on time.

- The notification is sent 24 hours before the appointment.
- The notification is sent 1 hour before the appointment.
- The notification includes the date and time of the appointment.
- The notification includes the location (e.g., outpatient clinic and room).
- The notification includes any specific instructions (e.g., fasting or bringing medication).
- For appointments that have already started, no notifications are sent.
- When an appointment is cancelled or modified within OpenMRS, notifications are either no longer sent or their scheduled sending times are adjusted accordingly.

### 2. Reporting & Billing

The communication module records whether a notification was successfully sent, so that reports can later be generated showing which notifications were sent on behalf of which organization and via which messaging provider.

- This should simplify the verification of invoices issued by messaging providers.

### 3. Provider Selection

As an OpenMRS organization, I want the communication module to use one of the supported messaging providers to send messages to my patients.

## Non-Functional Requirements

### 1. Independence & Integration

- The communication module must be able to operate independently and integrate with multiple OpenMRS instances.
- This allows different hospitals to use the module based on their own messaging provider subscriptions.
- The integration between the OpenMRS instance and the communication module must align with the stated objective.
- The integration must be documented for technical OpenMRS administrators and secured according to best practices appropriate for the chosen integration approach.

### 2. Compatibility

- Organizations using the communication module must be able to select one of the following supported messaging providers: SwiftSend, LegacyLink, AsyncFlow, and SecurePost.
- The communication module must support integration with OpenMRS version 2.7.x and above.
- The communication module must be able to process messages in multiple character sets.
- The communication module must support multiple time zones, ensuring all notifications and their scheduled send times respect the local time zone of the respective OpenMRS organization.
- The communication module must be designed in such a way that other functional OpenMRS modules (such as medical test result modules) can be integrated.

### 3. Security & Privacy

- Sensitive information regarding organizational subscriptions and platforms must be securely stored.
- In the event of unauthorized access, this information must not be usable.
- Authentication data for external messaging providers must not be stored in code or configuration files.
- All sensitive data (such as credentials, tokens, and message content) must be encrypted using at least AES-256 at rest and TLS 1.3 in transit.
- The communication module may process sensitive data but must never store it unencrypted, including in log files.
- The module must automatically delete patient-related and communication-related data within 14 days after processing.
- The module must retain metadata of sent messages for up to one year for traceability purposes.
- This metadata must not contain directly identifiable patient or appointment data, but must include sufficient information to verify messaging provider billing.

### 4. Standards & Reliability

- The module must comply with HL7 standards.
- HL7 systems support, among other things:
  - message reception and validation (structure, required fields, syntax checks)
  - acknowledgements (ACK) for delivery confirmation or error reporting
  - logging and tracking of messages for auditing and troubleshooting
  - message transformation (mapping between HL7 versions or local formats)
  - queuing and retry mechanisms in case of network failures

- The communication module must be implemented as a standalone process and be highly independent from other systems.
- Downtime in communication providers or OpenMRS instances must be handled via a self-designed and documented fallback or retry mechanism.
- The module’s operation must be fully observable via appropriate monitoring tooling, such as OpenTelemetry.
- A real-time dashboard must be available for OpenMRS administrators to monitor message status, system performance (throughput), and error reports for system oversight.

## Deliverables

The team must deliver:

- Documentation for technical administrators of an OpenMRS organization describing the steps and measures required to integrate the systems, including key considerations.
- A codebase runnable in a Docker environment, including setup instructions, a sample startup command, and a sample request demonstrating functionality.
- An Architectural Decision Record (ADR) log within the repository documenting key architectural decisions: the problem, considered options, and the rationale behind the chosen solution.
- A visualization of the communication module at application architecture level (C4 Levels 1, 2, and 3), plus a process flow visualization of how data moves through the system.
- A test report demonstrating reliability and extensibility.
- A project execution log containing:
  - Overview of development tools used (IDEs).
  - Overview of AI tools used (if applicable), including representative examples of usage (prompts, screenshots, logs).
  - Overview of commits per team member (including links where applicable).
