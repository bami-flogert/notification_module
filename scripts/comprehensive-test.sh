#!/usr/bin/env bash
# Comprehensive test runner (Linux/macOS/Git Bash). See docs/TEST_CHECKLIST.md.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PRODUCER_BASE="${PRODUCER_BASE:-http://127.0.0.1:5001}"
CONSUMER_BASE="${CONSUMER_BASE:-http://127.0.0.1:5002}"
API_KEY="${APIKEY_SEED_DEFAULT:-change-me-in-prod}"
SKIP_DOCKER_UP="${SKIP_DOCKER_UP:-0}"
METRICS_WAIT_MINUTES="${METRICS_WAIT_MINUTES:-15}"

PASSED=0
FAILED=0

pass() { PASSED=$((PASSED + 1)); echo "PASS $1 — $2"; }
fail() { FAILED=$((FAILED + 1)); echo "FAIL $1 — $2"; }

http_code() {
  curl -s -o /dev/null -w "%{http_code}" "$1" || echo "000"
}

wait_http() {
  local url="$1"
  local expect="${2:-200}"
  for _ in $(seq 1 60); do
    local code
    code="$(http_code "$url")"
    if [[ "$code" == "$expect" ]]; then return 0; fi
    sleep 2
  done
  return 1
}

query_prometheus() {
  curl -s --get --data-urlencode "query=$1" "http://127.0.0.1:9090/api/v1/query"
}

prom_max() {
  query_prometheus "$1" | python3 -c "
import json,sys
d=json.load(sys.stdin)
m=0.0
for r in d.get('data',{}).get('result',[]):
  if len(r.get('value',[]))>=2:
    m=max(m,float(r['value'][1]))
print(m)
" 2>/dev/null || echo 0
}

fix_rabbitmq_exchange_if_needed() {
  local cred
  cred="$(printf '%s' 'guest:guest' | base64 | tr -d '\n')"
  local type
  type="$(curl -s -u guest:guest "http://127.0.0.1:15672/api/exchanges/%2F/appointment.notifications" 2>/dev/null | python3 -c "import json,sys; d=json.load(sys.stdin); print(d.get('type',''))" 2>/dev/null || true)"
  if [[ "$type" == "fanout" ]]; then
    echo "==> Removing stale fanout exchange appointment.notifications"
    curl -s -u guest:guest -X DELETE "http://127.0.0.1:15672/api/exchanges/%2F/appointment.notifications" -o /dev/null -w "%{http_code}\n"
    docker compose --env-file env.example restart producer consumer
    sleep 15
  fi
}

echo "==> Comprehensive test run (root: $ROOT)"

if [[ "$SKIP_DOCKER_UP" != "1" ]]; then
  docker compose --env-file env.example up --build -d
fi

fix_rabbitmq_exchange_if_needed

echo "==> Infrastructure"
for c in notification-postgres rabbitmq notification-otel-collector notification-prometheus; do
  st="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}running{{end}}' "$c" 2>/dev/null || echo missing)"
  if [[ "$st" == "healthy" || "$st" == "running" ]]; then pass "I-$c" "container $c status=$st"; else fail "I-$c" "container $c status=$st"; fi
done

code="$(http_code http://127.0.0.1:1337/)"
[[ "$code" =~ ^[23] ]] && pass I3 "comworld $code" || fail I3 "comworld $code"

curl -sf http://127.0.0.1:8889/metrics >/dev/null && pass I4b "otel metrics" || fail I4b "otel metrics"
wait_http http://127.0.0.1:9090/-/ready 200 && pass I5 "prometheus ready" || fail I5 "prometheus ready"

echo "==> Health H1-H4"
wait_http "$PRODUCER_BASE/health" 200
for pair in "H1:$PRODUCER_BASE/health" "H2:$PRODUCER_BASE/ready" "H3:$CONSUMER_BASE/health" "H4:$CONSUMER_BASE/ready"; do
  id="${pair%%:*}"; url="${pair#*:}"
  wait_http "$url" 200 && pass "$id" "$url Healthy" || fail "$id" "$url"
done

echo "==> Endpoints & auth"
auth1="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PRODUCER_BASE/api/webhooks/openmrs/appointments/default" -H 'Content-Type: application/json' -d '{}')"
[[ "$auth1" == "401" ]] && pass AUTH1 "no key => 401" || fail AUTH1 "no key => $auth1"

auth2="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PRODUCER_BASE/api/webhooks/openmrs/appointments/default" -H 'Content-Type: application/json' -H 'X-Api-Key: wrong' -d '{}')"
[[ "$auth2" == "401" ]] && pass AUTH2 "wrong key => 401" || fail AUTH2 "wrong key => $auth2"

START_SOON="$(date -u -d '+70 minutes' '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null || date -u -v+70M '+%Y-%m-%dT%H:%M:%SZ')"
UUID="comprehensive-$(date +%s)"

webhook_code="$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PRODUCER_BASE/api/webhooks/openmrs/appointments/default" \
  -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' \
  -d "{\"event\":\"CREATED\",\"appointmentUuid\":\"$UUID\",\"status\":\"Scheduled\",\"startDateTime\":\"$START_SOON\",\"patientUuid\":\"p1\",\"patientName\":\"Test\",\"patientPhone\":\"+31612345678\",\"patientEmail\":\"t@example.com\",\"location\":\"Loc\",\"comments\":\"Test\"}")"
[[ "$webhook_code" == "202" ]] && pass E3 "webhook POST $webhook_code" || fail E3 "webhook POST $webhook_code"

c404="$(http_code "$CONSUMER_BASE/api/appointments")"
[[ "$c404" == "404" ]] && pass E17 "consumer 404" || fail E17 "consumer $c404"

echo "==> Database"
db_out="$(docker exec -i notification-postgres psql -U notification -d notification -t -A <<SQL
SELECT COUNT(*) FROM organizations WHERE "Key" = 'default';
SELECT COUNT(*) FROM organization_api_keys;
SELECT COUNT(*) FROM provider_secrets;
SELECT COUNT(*) FROM appointments WHERE "AppointmentUuid" = '$UUID';
SELECT COUNT(*) FROM scheduled_notifications sn JOIN appointments a ON a."Id" = sn."AppointmentId" WHERE a."AppointmentUuid" = '$UUID' AND sn."Status" = 'Pending';
SQL
)"
mapfile -t counts <<<"$db_out"
[[ "${counts[0]:-0}" -ge 1 ]] && pass DB4 "default org" || fail DB4 "default org"
[[ "${counts[1]:-0}" -ge 1 ]] && pass DB5 "api keys" || fail DB5 "api keys"
[[ "${counts[2]:-0}" -ge 1 ]] && pass DB6 "secrets" || fail DB6 "secrets"
[[ "${counts[3]:-0}" -ge 1 ]] && pass DB9 "appointment row" || fail DB9 "appointment row"
[[ "${counts[4]:-0}" -ge 2 ]] && pass DB9b "pending reminders" || fail DB9b "pending reminders"

echo "==> Pipeline (wait for consumer dispatch)"
dispatch_seen=0
for i in $(seq 1 24); do
  if docker compose --env-file env.example logs consumer --tail 100 2>/dev/null | grep -q "Sending via"; then
    dispatch_seen=1
    break
  fi
  echo "  waiting ($i/24)..."
  sleep 30
done
[[ "$dispatch_seen" -eq 1 ]] && pass MQ3 "consumer dispatch" || fail MQ3 "consumer dispatch"

queues="$(curl -s -u guest:guest http://127.0.0.1:15672/api/queues)"
echo "$queues" | grep -q 'notifications\.' && pass MQ1 "provider queues" || fail MQ1 "provider queues"

echo "==> Prometheus metrics"
wait_http http://127.0.0.1:9090/-/ready 200
ingest=0; dispatch=0; delivery=0; received=0
for i in $(seq 1 $((METRICS_WAIT_MINUTES * 4))); do
  ingest="$(prom_max 'increase(appointments_ingested_total[15m])')"
  dispatch="$(prom_max 'increase(notification_dispatch_dispatches_total[15m])')"
  delivery="$(prom_max 'increase(notification_delivery_success_deliveries_total[15m])')"
  received="$(prom_max 'increase(notification_messages_received_total[15m])')"
  echo "  attempt $i: ingest=$ingest dispatch=$dispatch delivery=$delivery received=$received"
  awk "BEGIN {exit !($ingest > 0 && $dispatch > 0 && $delivery > 0 && $received > 0)}" && break
  sleep 15
done
awk "BEGIN {exit !($ingest > 0)}" && pass M1 "ingest" || fail M1 "ingest=$ingest"
awk "BEGIN {exit !($dispatch > 0)}" && pass M10 "dispatch" || fail M10 "dispatch=$dispatch"
awk "BEGIN {exit !($delivery > 0)}" && pass M12 "delivery" || fail M12 "delivery=$delivery"
awk "BEGIN {exit !($received > 0)}" && pass M9 "received" || fail M9 "received=$received"

curl -sf http://127.0.0.1:9090/api/v1/rules | grep -q NotificationDeliveryFailureSpike && pass AL-rules "rules loaded" || fail AL-rules "rules"

curl -sf http://127.0.0.1:16686/api/services | grep -q notification-producer && pass T1 "jaeger services" || fail T1 "jaeger"
curl -sf http://127.0.0.1:3100/ready >/dev/null && pass L1 "loki ready" || fail L1 "loki"

echo "==> Summary PASS=$PASSED FAIL=$FAILED"
[[ "$FAILED" -eq 0 ]]
