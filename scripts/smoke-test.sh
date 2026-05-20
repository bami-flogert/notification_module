#!/usr/bin/env bash
# End-to-end smoke test (requires Docker daemon + docker compose).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Appointment ~70 minutes ahead so the 1h reminder becomes due in ~10 minutes.
if date -u -v+70M '+%Y-%m-%dT%H:%M:%SZ' >/dev/null 2>&1; then
  START_DATE_TIME="$(date -u -v+70M '+%Y-%m-%dT%H:%M:%SZ')"
else
  START_DATE_TIME="$(date -u -d '+70 minutes' '+%Y-%m-%dT%H:%M:%SZ')"
fi

echo "==> Building and starting stack (env from env.example)…"
docker compose --env-file env.example up --build -d

echo "==> Waiting for producer HTTP on :5001…"
for i in {1..60}; do
  code="$(curl -s -o /dev/null -w "%{http_code}" -X POST "http://127.0.0.1:5001/api/appointments" \
    -H "Content-Type: application/json" \
    -H "X-Api-Key: ${APIKEY_SEED_DEFAULT:-change-me-in-prod}" \
    -d '{}' || true)"
  if [[ "$code" != "000" ]]; then
    break
  fi
  sleep 2
done
if [[ "${code:-000}" == "000" ]]; then
  echo "Producer did not become reachable on :5001." >&2
  exit 1
fi

echo "==> POST sample appointment (startDateTime=${START_DATE_TIME})…"
curl -sS -X POST "http://localhost:5001/api/appointments/default" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${APIKEY_SEED_DEFAULT:-change-me-in-prod}" \
  -d "{
    \"appointmentUuid\": \"smoke-1\",
    \"organizationKey\": \"default\",
    \"patientUuid\": \"patient-1\",
    \"patientName\": \"Smoke Test\",
    \"patientPhone\": \"+31612345678\",
    \"patientEmail\": \"smoke@example.com\",
    \"startDateTime\": \"${START_DATE_TIME}\",
    \"status\": \"Confirmed\",
    \"location\": \"Smoke test location\",
    \"instructions\": \"Smoke test instructions\"
  }" | head -c 500
echo

echo "==> Waiting for scheduler + provider dispatch (up to 12 min)…"
for i in {1..24}; do
  if docker compose --env-file env.example logs consumer --tail 80 2>/dev/null \
    | grep -q "Sending via"; then
    echo "==> Provider dispatch observed in consumer logs."
    break
  fi
  sleep 30
done

echo "==> Consumer logs (last 40 lines)…"
docker compose --env-file env.example logs consumer --tail 40

echo "==> Done. Stop with: docker compose --env-file env.example down"
