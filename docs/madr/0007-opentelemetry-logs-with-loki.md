# 7. OpenTelemetry-logs naar Loki sturen


## Status

Goedgekeurd

## Context

De notificatiemodule stuurde al **traces** en **metrics** door via OpenTelemetry. Logs van de applicatie verschenen alleen in de console (`stdout`) en waren niet gekoppeld aan traces. Daardoor moesten beheerders handmatig zoeken in Jaeger wanneer er een probleem optrad.

Voor volledige observability zijn ook gekoppelde logs nodig.

## Beslissing

1. Applicatielogs worden voortaan ook via OpenTelemetry verstuurd.
2. Logs worden centraal opgeslagen in **Loki**.
3. **Grafana** wordt gekoppeld aan Loki zodat logs en traces eenvoudig samen bekeken kunnen worden.
4. Consolelogging blijft bestaan voor lokaal debuggen en `docker compose logs`.
5. De OpenTelemetry-configuratie wordt op één centrale plek beheerd.

## Gevolgen

**Positief**

* Traces, metrics en logs zitten samen in één observability-stack.
* Problemen zijn sneller te onderzoeken doordat logs direct gekoppeld zijn aan traces.
* Geen wijzigingen nodig aan bestaande logberichten.

**Negatief**

* Er draait een extra service (Loki), wat meer opslagruimte gebruikt.
* Voor volledige correlatie tussen logs en traces is Grafana/Loki nodig.