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

echo "==> Waiting for producer readiness on http://127.0.0.1:5001/health"
wait_for_service "http://127.0.0.1:5001/health" "Producer health endpoint"

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
  "appointmentUuid": "smoke-metrics-$(date +%s)",
  "organizationKey": "default",
  "patientUuid": "patient-smoke-metrics",
  "patientName": "Smoke Metrics",
  "patientPhone": "+31610000001",
  "patientEmail": "smoke-metrics@example.com",
  "startDateTime": "${START_DATE_TIME}",
  "status": "Confirmed",
  "location": "Smoke metrics location",
  "instructions": "Smoke metrics appointment"
}
EOF
)

echo "==> Posting test appointment: startDateTime=${START_DATE_TIME}"
curl -sS -X POST "http://127.0.0.1:5001/api/appointments/default" \
  -H "Content-Type: application/json" \
  -d "$APPOINTMENT_JSON"
echo

echo "==> Waiting for delivery metrics in Prometheus"
for i in {1..48}; do
  payload="$(query_prometheus 'increase(notification_delivery_success_deliveries_total[5m])')"
  value="$(printf '%s' "$payload" | metric_value)"
  echo "  attempt $i: delivery_success_increase=$value"
  if awk "BEGIN {exit !($value > 0)}" >/dev/null 2>&1; then
    echo "==> Delivery metric observed"
    break
  fi
  sleep 15
done

if awk "BEGIN {exit !($value > 0)}" >/dev/null 2>&1; then
  echo "==> Smoke metrics test passed"
  exit 0
fi

echo "==> Smoke metrics test failed: delivery metric did not appear"
exit 1
