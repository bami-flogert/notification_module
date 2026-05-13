#!/usr/bin/env bash
# End-to-end smoke test (requires Docker daemon + docker compose).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "==> Building and starting stack (env from env.example)…"
docker compose --env-file env.example up --build -d

echo "==> Waiting for producer HTTP on :5001…"
for i in {1..60}; do
  code="$(curl -s -o /dev/null -w "%{http_code}" -X POST "http://127.0.0.1:5001/api/appointments" \
    -H "Content-Type: application/json" \
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

echo "==> POST sample appointment…"
curl -sS -X POST "http://localhost:5001/api/appointments" \
  -H "Content-Type: application/json" \
  -d '{
    "appointmentUuid": "smoke-1",
    "patientUuid": "patient-1",
    "patientName": "Smoke Test",
    "patientPhone": "+31612345678",
    "patientEmail": "smoke@example.com",
    "startDateTime": "2026-05-12T14:30:00Z",
    "status": "Confirmed"
  }' | head -c 500
echo

echo "==> Consumer logs (last 40 lines)…"
docker compose --env-file env.example logs consumer --tail 40

echo "==> Done. Stop with: docker compose --env-file env.example down"
