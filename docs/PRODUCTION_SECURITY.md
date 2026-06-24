# Productie-deployment en beveiligingsgids

Dit document beschrijft hoe je de Notification Module veilig in productie uitrolt. Het gaat in op TLS-terminatie, encryptie van verbindingen naar RabbitMQ en PostgreSQL, en het roteren van geheimen.

---

## 1. Overzicht van de productie-stack

```
Internet
   │  HTTPS (TLS 1.3)
   ▼
[ Nginx reverse proxy ]
   │  HTTP (intern, geen TLS nodig tussen proxy en containers)
   ├──▶ Producer  (poort 5001)
   └──▶ Consumer  (poort 5002)
        │
        ├──▶ RabbitMQ  (TLS 5671)
        └──▶ PostgreSQL (TLS 5432)
```

Externe communicatie loopt altijd via de nginx-proxy met TLS 1.3. Interne containerverbindingen (Docker-netwerk) kunnen versleuteld worden wanneer het netwerk niet vertrouwd is.

---

## 2. TLS 1.3 met nginx (reverse proxy)

### 2.1 Vereisten

- Een geldig TLS-certificaat (Let's Encrypt of organisatie-CA).
- nginx ≥ 1.25 (ondersteunt TLS 1.3 standaard).

### 2.2 Nginx-configuratie

Sla de onderstaande configuratie op als `/etc/nginx/conf.d/notification-module.conf`:

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name notification.jouwdomein.nl;

    ssl_certificate     /etc/ssl/certs/notification.crt;
    ssl_certificate_key /etc/ssl/private/notification.key;

    # Alleen TLS 1.3 toestaan
    ssl_protocols TLSv1.3;
    ssl_prefer_server_ciphers off;

    # HSTS (verplicht HTTPS minimaal 1 jaar)
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    # Producer (webhooks + API)
    location /api/ {
        proxy_pass http://producer:5001;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}

# HTTP → HTTPS redirect
server {
    listen 80;
    server_name notification.jouwdomein.nl;
    return 301 https://$host$request_uri;
}
```

### 2.3 `ASPNETCORE_URLS` versus ingebouwde HTTPS

De producer en consumer zijn geconfigureerd om te luisteren op HTTP (`http://+:5001` en `http://+:5002`). TLS wordt **buiten** de container afgehandeld door nginx. Wijzig `ASPNETCORE_URLS` **niet** naar `https://` tenzij je ook een certificaat in de container beschikbaar maakt — dat voegt complexiteit toe zonder extra veiligheid achter de proxy.

In `docker-compose.prod.yml` (zie §6) zijn de poorten van producer en consumer **niet** blootgesteld aan de host; alleen nginx is extern bereikbaar.

---

## 3. RabbitMQ TLS

### 3.1 RabbitMQ-configuratie

Voeg het volgende toe aan `rabbitmq.conf` in de RabbitMQ-container:

```ini
listeners.ssl.default = 5671
ssl_options.cacertfile = /etc/rabbitmq/ca.crt
ssl_options.certfile   = /etc/rabbitmq/server.crt
ssl_options.keyfile    = /etc/rabbitmq/server.key
ssl_options.verify     = verify_peer
ssl_options.fail_if_no_peer_cert = false
```

### 3.2 Verbindingsstring in de module

Pas `RabbitMq__Host` aan en zet de poort op `5671`:

```
RabbitMq__Host=rabbitmq
RabbitMq__Port=5671
RabbitMq__UseTls=true
```

> **Let op:** de huidige `RabbitMqConnectionFactory` in `NotificationModule.Shared` leest `RabbitMq__UseTls`. Controleer of `Ssl.Enabled = true` en `Ssl.ServerName` ingesteld zijn als je TLS activeert.

---

## 4. PostgreSQL TLS

### 4.1 PostgreSQL-configuratie

In `postgresql.conf`:

```ini
ssl = on
ssl_cert_file = 'server.crt'
ssl_key_file  = 'server.key'
```

In `pg_hba.conf`, vervang `host` door `hostssl` voor de `notification`-gebruiker:

```
hostssl  notification_module  notification  0.0.0.0/0  scram-sha-256
```

### 4.2 Connection string

Voeg `sslmode=require` toe:

```
NotificationDb__ConnectionString=Host=postgres;Port=5432;Database=notification_module;Username=notification;Password=...;Ssl Mode=Require;Trust Server Certificate=false
```

---

## 5. Geheimen en rotatie

### 5.1 Master key (`SECRETS_MASTER_KEY_BASE64`)

De master key versleutelt alle provider-credentials in de database (AES-256-GCM). Genereer een sterke sleutel:

```bash
openssl rand -base64 32
```

Sla de sleutel op in een geheimenbeheerder (bijv. HashiCorp Vault, Azure Key Vault, AWS Secrets Manager) en injecteer hem als omgevingsvariabele in de container. **Bewaar de sleutel nooit in `appsettings.json`, Dockerfile of versiebeheersysteem.**

#### Rotatieprocedure

1. Genereer een nieuwe master key.
2. Stop de consumer (geen nieuwe leveringen).
3. Draai een migratiescript dat alle `provider_secrets`-rijen ontsleutelt met de oude key en opnieuw versleutelt met de nieuwe key.
4. Vervang `SECRETS_MASTER_KEY_BASE64` in de omgeving van **zowel** producer als consumer.
5. Herstart consumer en producer.
6. Verifieer via `/ready` dat de health-checks groen zijn.

### 5.2 API-sleutels per organisatie

API-sleutels worden gehashed (BCrypt) opgeslagen in de `organization_api_keys`-tabel. Een sleutel is eenmalig leesbaar bij uitgifte.

#### Rotatieprocedure

1. Genereer een nieuwe sleutel (willekeurige string, minimaal 32 tekens).
2. Stuur `PUT /api/organizations/{key}/providers` of gebruik een beheerdersscript om de nieuwe sleutel te hashen en op te slaan.
3. Communiceer de nieuwe sleutel veilig aan de OpenMRS-beheerder.
4. Zet de oude sleutel op inactief of verwijder hem.

### 5.3 `SecretsSeed__*` — uitsluitend voor ontwikkeling

De `SecretsSeed__*`-variabelen (bijv. `SECRETS_SEED_SWIFTSEND_API_KEY`) zijn bedoeld om lokale ontwikkelomgevingen snel op te starten. Gebruik ze **nooit** in productie: de seed wordt maar één keer geschreven en is daarna niet meer nodig.

```
# env.example — alleen lokale ontwikkeling, niet voor productie
SECRETS_SEED_SWIFTSEND_API_KEY=your-api-key-here   # DEV ONLY
```

In productie vul je de `provider_secrets`-tabel via de beheerdersinterface of een beveiligd script.

---

## 6. `docker-compose.prod.yml`

Gebruik dit bestand als startpunt voor een productie-deployment. Kopieer het naar de server en pas waarden aan.

```yaml
services:
  nginx:
    image: nginx:1.27-alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/notification-module.conf:/etc/nginx/conf.d/default.conf:ro
      - /etc/ssl/certs/notification.crt:/etc/ssl/certs/notification.crt:ro
      - /etc/ssl/private/notification.key:/etc/ssl/private/notification.key:ro
    depends_on:
      - producer
    restart: unless-stopped

  producer:
    image: ghcr.io/jouworg/notification-producer:latest
    expose:
      - "5001"         # alleen intern, niet naar host
    environment:
      ASPNETCORE_URLS: "http://+:5001"
      NotificationDb__ConnectionString: "${NOTIFICATION_DB_CONNECTION}"
      RabbitMq__Host: rabbitmq
      RabbitMq__Port: "5671"
      RabbitMq__UseTls: "true"
      Secrets__MasterKeyBase64: "${SECRETS_MASTER_KEY_BASE64}"
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    restart: unless-stopped

  consumer:
    image: ghcr.io/jouworg/notification-consumer:latest
    expose:
      - "5002"
    environment:
      NotificationDb__ConnectionString: "${NOTIFICATION_DB_CONNECTION}"
      RabbitMq__Host: rabbitmq
      RabbitMq__Port: "5671"
      RabbitMq__UseTls: "true"
      Secrets__MasterKeyBase64: "${SECRETS_MASTER_KEY_BASE64}"
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    restart: unless-stopped

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: notification
      POSTGRES_PASSWORD: "${POSTGRES_PASSWORD}"
      POSTGRES_DB: notification_module
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U notification"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    environment:
      RABBITMQ_DEFAULT_USER: "${RABBITMQ_USER}"
      RABBITMQ_DEFAULT_PASS: "${RABBITMQ_PASS}"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  postgres_data:
  rabbitmq_data:
```

> **Opmerking:** poorten van producer en consumer zijn met `expose` (niet `ports`) gedeclareerd, zodat ze alleen bereikbaar zijn voor de nginx-container op het interne Docker-netwerk.

---

## 7. Checklist vóór go-live

- [ ] TLS-certificaat geldig en automatisch vernieuwd (bijv. Certbot).
- [ ] `ssl_protocols TLSv1.3` actief — verifieer met `nmap --script ssl-enum-ciphers -p 443 <host>`.
- [ ] `SECRETS_MASTER_KEY_BASE64` afkomstig uit geheimenbeheerder, niet uit `.env`-bestand op disk.
- [ ] `POSTGRES_PASSWORD` en `RABBITMQ_PASS` sterk en uniek.
- [ ] Geen `SecretsSeed__*` variabelen aanwezig in de productieomgeving.
- [ ] `APIKEY_SEED_DEFAULT` is `change-me-in-prod` vervangen door een sterke sleutel.
- [ ] Health-endpoints bereikbaar: `https://notification.jouwdomein.nl/health` en `/ready`.
- [ ] Grafana en RabbitMQ Management niet publiek bereikbaar (VPN of firewallregel).

---

## Gerelateerd

- [README.md](../README.md) — opstarten en overzicht
- [docs/RELIABILITY.md](RELIABILITY.md) — DLQ en retries
- [docs/LOGGING.md](LOGGING.md) — log-redactie en observability
- [env.example](../env.example) — lokale ontwikkelvariabelen (niet voor productie)
