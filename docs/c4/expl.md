# C4 Model  Communicatiemodule

---

## C1 Context diagram

Toont het systeem op het hoogste niveau: wie gebruikt het en met welke externe systemen communiceert het.

![c1](c1_v2.svg)

**Actoren:** Patiënt (ontvangt notificaties), Beheerder (monitort dashboard), OpenMRS organisatie (levert afspraken aan).

**Externe systemen:** SwiftSend, LegacyLink, AsyncFlow en SecurePost — de messaging providers die berichten afleveren.

---

## C2 Container diagram

Toont de deploybare onderdelen van het systeem en hoe ze communiceren.

![c2](c2_v2.svg)

| Container                 | Verantwoordelijkheid                                                         |
| ------------------------- | ---------------------------------------------------------------------------- |
| Producer API              | Ontvangt afspraken via REST en plant notificaties in de database             |
| Scheduler                 | Pollt de database en publiceert berichten naar RabbitMQ op het juiste moment |
| Message Broker (RabbitMQ) | Verdeelt berichten via fanout exchange naar vier provider-queues             |
| Consumer                  | Leest queues uit, verstuurt naar providers en logt het resultaat             |
| Database (PostgreSQL)     | Slaat afspraken, notificaties, delivery-logs en versleutelde secrets op      |
