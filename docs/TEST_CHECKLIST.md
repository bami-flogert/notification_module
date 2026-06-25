# Comprehensive Test Checklist — Notification Module

Runnable automation: **`scripts/comprehensive-test.ps1`** (Windows) and **`scripts/comprehensive-test.sh`** (Linux/macOS/Git Bash).

## Quick start

```powershell
# Windows (stack must be running or use -SkipDockerUp after manual up)
docker compose --env-file env.example up --build -d
dotnet test NotificationModule.sln
.\scripts\comprehensive-test.ps1
```

```bash
docker compose --env-file env.example up --build -d
dotnet test NotificationModule.sln
./scripts/comprehensive-test.sh
```

## Test matrix

| Section | IDs | Automated by script |
|---------|-----|---------------------|
| Unit tests | A1 | `dotnet test` |
| Infrastructure | I1–I6 | Docker health + HTTP probes |
| Health endpoints | H1–H4 | curl/Invoke-WebRequest |
| FHIR / JSON API | E1, E3, E7–E8, E13, E15, E17 | POST/GET matrix |
| Auth | AUTH1–AUTH3 | 401/202 scenarios |
| Database | DB4–DB6, DB9, DB16, SEC1 | psql via docker exec |
| Pipeline | MQ3 | consumer log grep |
| Metrics | M1, M9–M10, M12, AL-rules | Prometheus API |
| Traces | T1 | Jaeger `/api/services` |
| Logs | L1 | Loki `/ready` |
| Security | SEC1, SEC3 | SQL + public metadata |

Manual follow-ups (not in automated script): H5 (stop Postgres/RabbitMQ), AUTH4 (disabled org in DB), P2–P6 (each ComWorld provider), N1–N7 (chaos/resilience), AL1–AL4 (simulate alert firing), Grafana dashboard panels G2–G3, Jaeger end-to-end trace T2–T5 (search `appointment.uuid` in UI).

The script auto-repairs a stale RabbitMQ `appointment.notifications` exchange when it was created as `fanout` (deletes via management API and restarts producer/consumer).

See project [README.md](../README.md), [openmrs/OMOD_BRIDGE.md](openmrs/OMOD_BRIDGE.md), [DASHBOARD_DATABASE.md](DASHBOARD_DATABASE.md).
