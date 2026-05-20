# Failure Mode & Effect Analysis (FMEA)

Per component wordt in kaart gebracht wat er mis kan gaan, wat het gevolg is, wat de oorzaak is en welke maatregel is genomen om de kans of impact te verkleinen.

---

## Component: Producer API 

| Failure mode                                          | Effect                                                      | Oorzaak                                                        | Maatregel                                                                                                                                    |
| ----------------------------------------------------- | ----------------------------------------------------------- | -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| Ongeldige afspraken worden geaccepteerd               | Onnodige notificaties worden aangemaakt en verstuurd        | Geen validatie op verplichte velden of datum in verleden       | Input validatie op `appointmentUuid`, `startDateTime` en `status`; afspraken in het verleden worden genegeerd                          |
| Afspraak wordt dubbel ingediend                       | Dubbele notificaties naar patiënt                          | OpenMRS stuurt hetzelfde request meerdere keren (retry of bug) | Idempotente verwerking op `appointmentUuid`; bestaande afspraak wordt geüpdatet, niet opnieuw aangemaakt                                  |
| Scheduler publiceert bericht te laat of helemaal niet | Patiënt ontvangt notificatie niet op tijd of niet          | Poll-interval te hoog (standaard 30s) of scheduler crasht      | Durable `scheduled_notifications` tabel als fallback; scheduler herstart automatisch; status blijft `Pending` tot succesvol gepubliceerd |
| RabbitMQ tijdelijk niet beschikbaar bij publicatie    | Bericht wordt niet gepubliceerd, notificatie niet verstuurd | RabbitMQ container herstart of netwerkfout                     | `EnsureConnectedWithRetry()` herverbindt automatisch; `Pending`-status in DB zorgt dat scheduler het opnieuw probeert                    |

---

## Component: RabbitMQ 

| Failure mode                                     | Effect                                           | Oorzaak                                           | Maatregel                                                                                           |
| ------------------------------------------------ | ------------------------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Berichten gaan verloren bij herstart             | Notificaties worden nooit verstuurd              | Queues of exchange niet persistent geconfigureerd | Exchange en queues gedeclareerd met `durable: true`; berichten verstuurd met `persistent: true` |
| RabbitMQ start later op dan Producer of Consumer | Verbindingsfout bij opstarten, module werkt niet | Race condition in Docker Compose opstartvolgorde  | Producer en Consumer bevatten retry-loop (`WaitForRabbitMqAsync`) die elke 3 seconden herprobeert |

---

## Component: Consumer / Dispatcher

| Failure mode                                            | Effect                                              | Oorzaak                                                                 | Maatregel                                                                                                                                                           |
| ------------------------------------------------------- | --------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Provider-API geeft foutmelding of is niet bereikbaar    | Notificatie niet afgeleverd bij patiënt            | Tijdelijke downtime bij SwiftSend / LegacyLink / AsyncFlow / SecurePost | Resultaat wordt gelogd als `Failed` in `notification_deliveries`; status in `scheduled_notifications` wordt `Failed`; zichtbaar in dashboard voor beheerder |
| Bericht kan niet worden gedeserialiseerd (corrupt JSON) | Bericht wordt verworpen zonder verwerking           | Incompatibele berichtversie of corruptie in queue                       | `BasicNack` met `requeue: false` zodat corrupt bericht niet de queue blokkeert; fout gelogd via `_logger.LogError`                                            |
| Consumer crasht midden in verwerking                    | Bericht blijft onbevestigd in queue                 | Onverwachte exception, geheugenprobleem of container-herstart           | `autoAck: false` — bericht wordt pas ge-acked na succesvolle verwerking; bij herstart pakt Consumer het bericht opnieuw op uit de queue                          |
| Verkeerde provider-secrets gebruikt                     | Authenticatiefout bij provider, notificatie mislukt | Fout in omgevingsvariabelen of seed-data bij opstarten                  | Secrets worden bij lege tabel eenmalig versleuteld ingeladen via `SecretsSeed`; AES-256-GCM encryptie; master key alleen via omgevingsvariabele                   |

---

## Component: PostgreSQL 

| Failure mode                                         | Effect                                                             | Oorzaak                                        | Maatregel                                                                                                                                 |
| ---------------------------------------------------- | ------------------------------------------------------------------ | ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Database tijdelijk niet bereikbaar                   | Afspraken kunnen niet opgeslagen worden; scheduler kan niet pollen | Container herstart, netwerk- of schijffout     | Docker Compose healthcheck zorgt dat Producer en Consumer pas starten als Postgres klaar is; retry-logica in verbinding                   |
| Patiëntgegevens blijven langer dan 14 dagen bewaard | Schending van privacy-eis uit opdracht                             | Geen automatische opschoning geïmplementeerd  | ⚠️ Nog te implementeren: scheduled job die rijen ouder dan 14 dagen verwijdert uit `appointments` en gerelateerde tabellen            |
| Gevoelige data terechtkomt in logbestanden           | Privacy-lek in logbestanden                                        | Patiëntgegevens per ongeluk in logs opgenomen | `notification_deliveries` bevat geen direct identificeerbare gegevens; alleen provider, status, tijdstip en `scheduledNotificationId` |
