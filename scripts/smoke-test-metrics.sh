#!/usr/bin/env bash
# Smoke test: start the stack, post an appointment, and wait until Prometheus sees delivery metrics.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [ ! -f env.example ]; then
  echo "env.example not found in $ROOT" >&2
  exit 1
fi

query_prometheus() {
  local query="$1"
  curl -s --get --data-urlencode "query=${query}" "http://127.0.0.1:9090/api/v1/query"
}

metric_value() {
  local max=0
  local line
  while IFS= read -r line; do
    if printf '%s\n' "$line" | grep -q '"value":\['; then
      local value
      value=$(printf '%s\n' "$line" | sed -E 's/.*"value":\[[^,]*,"([^"]+)"\].*/\1/')
      if [ -n "$value" ]; then
        if printf '%s\n%s\n' "$max" "$value" | sort -n | tail -1 | grep -q "^$value$"; then
          max="$value"
        fi
      fi
    fi
  done
  printf '%s\n' "$max"
}

wait_for_service() {
  local url="$1"
  local name="$2"
  for i in {1..60}; do
    status=$(curl -s -o /dev/null -w "%{http_code}" "$url" || echo 000)
    if echo "$status" | grep -qE '^(200|400|404|405)$'; then
      echo "$name is reachable"
      return 0
    fi
    sleep 2
  done
  echo "$name did not become reachable at $url" >&2
  return 1
}

echo "==> Starting stack with docker compose"
docker compose --env-file env.example up --build -d

echo "==> Waiting for producer readiness on http://127.0.0.1:5001/ready"
wait_for_service "http://127.0.0.1:5001/ready" "Producer ready endpoint"

echo "==> Waiting for consumer readiness on http://127.0.0.1:5002/ready"
wait_for_service "http://127.0.0.1:5002/ready" "Consumer ready endpoint"

echo "==> Waiting for Prometheus on http://127.0.0.1:9090"
wait_for_service "http://127.0.0.1:9090/-/ready" "Prometheus"

# Appointment starts 70 minutes in the future so the 1h reminder becomes due soon.
if date -u -v+70M '+%Y-%m-%dT%H:%M:%SZ' >/dev/null 2>&1; then
  START_DATE_TIME="$(date -u -v+70M '+%Y-%m-%dT%H:%M:%SZ')"
else
  START_DATE_TIME="$(date -u -d '+70 minutes' '+%Y-%m-%dT%H:%M:%SZ')"
fi

APPOINTMENT_JSON=$(cat <<EOF
{
  "event": "CREATED",
  "appointmentUuid": "smoke-metrics-$(date +%s)",
  "status": "Scheduled",
  "startDateTime": "${START_DATE_TIME}",
  "patientUuid": "patient-smoke-metrics",
  "patientName": "Smoke Metrics",
  "patientPhone": "+31610000001",
  "patientEmail": "smoke-metrics@example.com",
  "location": "Smoke metrics location",
  "comments": "Smoke metrics appointment"
}
EOF
)

API_KEY="${APIKEY_SEED_DEFAULT:-change-me-in-prod}"

echo "==> Posting test appointment: startDateTime=${START_DATE_TIME}"
curl -sS -X POST "http://127.0.0.1:5001/api/webhooks/openmrs/appointments/default" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY}" \
  -d "$APPOINTMENT_JSON"
echo

echo "==> Waiting for ingest metric (soon after POST)"
ingest_value=0
for i in {1..12}; do
  ingest_payload="$(query_prometheus 'increase(appointments_ingested_total[5m])')"
  ingest_value="$(printf '%s' "$ingest_payload" | metric_value)"
  echo "  ingest attempt $i: increase=$ingest_value"
  if awk "BEGIN {exit !($ingest_value > 0)}" >/dev/null 2>&1; then
    echo "==> Ingest metric observed"
    break
  fi
  sleep 5
done

if ! awk "BEGIN {exit !($ingest_value > 0)}" >/dev/null 2>&1; then
  echo "==> Smoke metrics test failed: ingest=$ingest_value (expected soon after POST)"
  exit 1
fi

echo "==> Waiting for scheduler pipeline metrics (dispatch, delivery, received)"
dispatch_value=0
delivery_value=0
received_value=0
for i in {1..48}; do
  dispatch_payload="$(query_prometheus 'increase(notification_dispatch_dispatches_total[5m])')"
  dispatch_value="$(printf '%s' "$dispatch_payload" | metric_value)"
  delivery_payload="$(query_prometheus 'increase(notification_delivery_success_deliveries_total[5m])')"
  delivery_value="$(printf '%s' "$delivery_payload" | metric_value)"
  received_payload="$(query_prometheus 'increase(notification_messages_received_total[5m])')"
  received_value="$(printf '%s' "$received_payload" | metric_value)"
  echo "  pipeline attempt $i: dispatch=$dispatch_value delivery=$delivery_value received=$received_value"
  if awk "BEGIN {exit !($dispatch_value > 0 && $delivery_value > 0 && $received_value > 0)}" >/dev/null 2>&1; then
    echo "==> Pipeline metrics observed (dispatch, delivery, received)"
    break
  fi
  sleep 15
done

if awk "BEGIN {exit !($dispatch_value > 0 && $delivery_value > 0 && $received_value > 0)}" >/dev/null 2>&1; then
  echo "==> Smoke metrics test passed (ingest=$ingest_value dispatch=$dispatch_value delivery=$delivery_value received=$received_value)"
  exit 0
fi

echo "==> Smoke metrics test failed: ingest=$ingest_value dispatch=$dispatch_value delivery=$delivery_value received=$received_value"
exit 1
