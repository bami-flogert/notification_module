# ADR: Dead-letter queues voor mislukte berichten

## Status

Accepted

## Context

Berichten in RabbitMQ kunnen mislukken: ongeldige inhoud, tijdelijke storingen of fouten in de verwerking. Zonder aparte opslag gaan die berichten verloren zodra de consumer ze afwijst. Voor een notificatiemodule is dat onwenselijk: beheerders moeten kunnen zien wat misging en een bericht opnieuw kunnen aanbieden.

## Besluit

We gebruiken per provider-queue een **dead-letter queue** (DLQ), bijvoorbeeld `notifications.swiftsend.dlq`.

- Ongeldige berichten (niet te lezen als afspraakbericht) gaan **direct** naar de DLQ.
- Bij andere verwerkingsfouten proberen we het bericht **maximaal drie keer**; daarna gaat het naar de DLQ.
- Berichten worden **expliciet** naar de DLQ gepubliceerd en daarna uit de hoofdwachtrij gehaald, zodat bestaande wachtrijen in Docker niet opnieuw aangemaakt hoeven te worden.

De volgende optie is overwogen maar niet gekozen:

- Alleen RabbitMQ “dead-letter exchange” op de hoofdwachtrij: vereist andere queue-instellingen en maakt bestaande omgevingen lastiger bij te werken.

## Gevolgen

### Positief

- Geen stille dataverlies meer bij fouten.
- Beheerders kunnen DLQ-berichten inspecteren en handmatig opnieuw versturen.
- Meetbaar via `notification_messages_dlq_total` in Grafana.

### Negatief

- DLQ’s moeten periodiek worden opgeschoond of opnieuw verwerkt, anders groeien ze.
- Iets meer logica in de consumer (retry-teller en DLQ-publish).
