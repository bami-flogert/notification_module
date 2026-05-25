# Comprehensive test runner for notification_module (Windows PowerShell).
# Implements the test matrix from TEST_CHECKLIST.md / project plan.
param(
    [string]$ProducerBase = "http://127.0.0.1:5001",
    [string]$ConsumerBase = "http://127.0.0.1:5002",
    [string]$ApiKey = "change-me-in-prod",
    [switch]$SkipDockerUp,
    [switch]$SkipMetricsWait,
    [int]$MetricsWaitMinutes = 15
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$script:Passed = 0
$script:Failed = 0
$script:Skipped = 0
$script:Results = [System.Collections.Generic.List[string]]::new()

function Write-TestResult {
    param([string]$Id, [bool]$Ok, [string]$Detail)
    if ($Ok) {
        $script:Passed++
        $line = "PASS $Id - $Detail"
    } else {
        $script:Failed++
        $line = "FAIL $Id - $Detail"
    }
    $script:Results.Add($line)
    Write-Host $line
}

function Wait-HttpStatus {
    param(
        [string]$Url,
        [int[]]$AcceptCodes = @(200),
        [int]$MaxAttempts = 60,
        [int]$DelaySeconds = 2
    )
    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($AcceptCodes -contains $r.StatusCode) { return $r }
        } catch {
            if ($_.Exception.Response -and ($AcceptCodes -contains [int]$_.Exception.Response.StatusCode)) {
                return $_.Exception.Response
            }
        }
        Start-Sleep -Seconds $DelaySeconds
    }
    return $null
}

function Invoke-JsonPost {
    param(
        [string]$Url,
        [string]$Body,
        [hashtable]$Headers = @{},
        [string]$ContentType = "application/json"
    )
    $h = @{ "Content-Type" = $ContentType }
    foreach ($k in $Headers.Keys) { $h[$k] = $Headers[$k] }
    return Invoke-WebRequest -Uri $Url -Method POST -Body $Body -Headers $h -UseBasicParsing -TimeoutSec 30
}

function Query-Prometheus {
    param([string]$Query)
    $encoded = [uri]::EscapeDataString($Query)
    $url = "http://127.0.0.1:9090/api/v1/query?query=$encoded"
    $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
    return $r.Content | ConvertFrom-Json
}

function Get-ResponseText {
    param($Response)
    if ($Response.Content -is [byte[]]) {
        return [System.Text.Encoding]::UTF8.GetString($Response.Content)
    }
    return [string]$Response.Content
}

function Get-PromMaxValue {
    param($PromResult)
    $max = 0.0
    if ($PromResult.status -ne "success") { return $max }
    foreach ($res in $PromResult.data.result) {
        if ($res.value.Count -ge 2) {
            $v = [double]$res.value[1]
            if ($v -gt $max) { $max = $v }
        }
    }
    return $max
}

function Test-DockerHealth {
    param([string]$ContainerName)
    $status = docker inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{if .State.Running}}running{{else}}stopped{{end}}{{end}}" $ContainerName 2>$null
    return ($status -eq "healthy" -or $status -eq "running")
}

function Repair-RabbitMqExchangeIfNeeded {
    try {
        $ex = Invoke-RestMethod -Uri "http://127.0.0.1:15672/api/exchanges/%2F/appointment.notifications" `
            -Headers @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest")) }
        if ($ex.type -eq "fanout") {
            Write-Host "==> Removing stale fanout exchange appointment.notifications"
            Invoke-WebRequest -Uri "http://127.0.0.1:15672/api/exchanges/%2F/appointment.notifications" -Method DELETE `
                -Headers @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest")) } `
                -UseBasicParsing | Out-Null
            docker compose --env-file env.example restart producer consumer 2>&1 | Out-Host
            Start-Sleep -Seconds 15
        }
    } catch {
        Write-Host "==> RabbitMQ exchange check skipped: $($_.Exception.Message)"
    }
}

Write-Host "==> Comprehensive test run (root: $Root)"

if (-not $SkipDockerUp) {
    Write-Host "==> Starting stack (docker compose --env-file env.example up -d)"
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        docker compose --env-file env.example up -d 2>&1 | Out-Host
    } finally {
        $ErrorActionPreference = $prevEap
    }
}

Repair-RabbitMqExchangeIfNeeded

Write-Host "==> Waiting for producer ready (migrations)"
$producerReady = Wait-HttpStatus -Url "$ProducerBase/ready" -AcceptCodes @(200) -MaxAttempts 60
if (-not $producerReady) {
    Write-Host "WARN: producer /ready not healthy before infrastructure checks"
}

Write-Host "==> Waiting for consumer ready"
$consumerReady = Wait-HttpStatus -Url "$ConsumerBase/ready" -AcceptCodes @(200) -MaxAttempts 30
if (-not $consumerReady) {
    Write-Host "==> Restarting consumer (likely started before DB migrations finished)"
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { docker compose --env-file env.example restart consumer 2>&1 | Out-Host } finally { $ErrorActionPreference = $prevEap }
    $consumerReady = Wait-HttpStatus -Url "$ConsumerBase/ready" -AcceptCodes @(200) -MaxAttempts 30
}
if (-not $consumerReady) {
    Write-Host "WARN: consumer /ready not healthy before infrastructure checks"
}

# --- Infrastructure I1-I6 ---
Write-Host "`n==> Infrastructure health (I1-I6)"
$containers = @(
    @{ Id = "I1"; Name = "notification-postgres" },
    @{ Id = "I2"; Name = "rabbitmq" },
    @{ Id = "I4"; Name = "notification-otel-collector" },
    @{ Id = "I5"; Name = "notification-prometheus" }
)
foreach ($c in $containers) {
    $ok = Test-DockerHealth -ContainerName $c.Name
    Write-TestResult -Id $c.Id -Ok $ok -Detail "container $($c.Name) health=$ok"
}

try {
    $cw = Invoke-WebRequest -Uri "http://127.0.0.1:1337" -UseBasicParsing -TimeoutSec 5
    Write-TestResult -Id "I3" -Ok ($cw.StatusCode -ge 200) -Detail "comworld HTTP $($cw.StatusCode)"
} catch {
    Write-TestResult -Id "I3" -Ok $false -Detail "comworld unreachable: $($_.Exception.Message)"
}

try {
    $otel = Invoke-WebRequest -Uri "http://127.0.0.1:8889/metrics" -UseBasicParsing -TimeoutSec 5
    $body = [string]$otel.Content
    $hasMetrics = $body.Length -gt 0 -and ($body -match "otel|prometheus|# HELP")
    Write-TestResult -Id "I4b" -Ok $hasMetrics -Detail "OTEL :8889/metrics length=$($body.Length)"
} catch {
    Write-TestResult -Id "I4b" -Ok $false -Detail "OTEL metrics: $($_.Exception.Message)"
}

foreach ($pair in @(
    @{ Id = "I6a"; Url = "http://127.0.0.1:16686"; Name = "jaeger"; Retries = 1 },
    @{ Id = "I6b"; Url = "http://127.0.0.1:3100/ready"; Name = "loki"; Retries = 10 },
    @{ Id = "I6c"; Url = "http://127.0.0.1:3000/login"; Name = "grafana"; Retries = 1 }
)) {
    $ok = $false
    $detail = "$($pair.Name) unreachable"
    for ($i = 1; $i -le $pair.Retries; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $pair.Url -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -lt 500) {
                $ok = $true
                $detail = "$($pair.Name) HTTP $($r.StatusCode)"
                break
            }
            $detail = "$($pair.Name) HTTP $($r.StatusCode)"
        } catch {
            $detail = "$($pair.Name): $($_.Exception.Message)"
        }
        if ($i -lt $pair.Retries) { Start-Sleep -Seconds 3 }
    }
    Write-TestResult -Id $pair.Id -Ok $ok -Detail $detail
}

# --- Health probes H1-H4 ---
Write-Host "`n==> Application health (H1-H4)"
Wait-HttpStatus -Url "$ProducerBase/fhir/metadata" -AcceptCodes @(200) | Out-Null
foreach ($pair in @(
    @{ Id = "H1"; Url = "$ProducerBase/health" },
    @{ Id = "H2"; Url = "$ProducerBase/ready" },
    @{ Id = "H3"; Url = "$ConsumerBase/health" },
    @{ Id = "H4"; Url = "$ConsumerBase/ready" }
)) {
    $r = Wait-HttpStatus -Url $pair.Url -AcceptCodes @(200) -MaxAttempts 30
    $body = if ($r) { $r.Content.Trim() } else { "no response" }
    Write-TestResult -Id $pair.Id -Ok ($null -ne $r -and $body -eq "Healthy") -Detail "$($pair.Url) => $body"
}

# --- E1 metadata ---
Write-Host "`n==> Endpoints (E1-E17, AUTH)"
$meta = Invoke-WebRequest -Uri "$ProducerBase/fhir/metadata" -Headers @{ Accept = "application/fhir+json" } -UseBasicParsing
$metaBody = Get-ResponseText $meta
$metaOk = ($meta.StatusCode -eq 200) -and ($metaBody -match "CapabilityStatement")
Write-TestResult -Id "E1" -Ok $metaOk -Detail "GET /fhir/metadata => $($meta.StatusCode)"

# AUTH1 - no key
try {
    $bad = Invoke-JsonPost -Url "$ProducerBase/api/appointments/default" -Body '{"appointmentUuid":"auth1"}' -Headers @{}
    Write-TestResult -Id "AUTH1" -Ok $false -Detail "expected 401 got $($bad.StatusCode)"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-TestResult -Id "AUTH1" -Ok ($code -eq 401) -Detail "no API key => $code"
}

# AUTH2 - wrong key
try {
    $bad = Invoke-JsonPost -Url "$ProducerBase/api/appointments/default" -Body '{"appointmentUuid":"auth2"}' -Headers @{ "X-Api-Key" = "wrong-key" }
    Write-TestResult -Id "AUTH2" -Ok $false -Detail "expected 401 got $($bad.StatusCode)"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-TestResult -Id "AUTH2" -Ok ($code -eq 401) -Detail "wrong API key => $code"
}

# AUTH3 - valid key via X-Organization-Key header (default org)
$startFar = (Get-Date).ToUniversalTime().AddDays(3).ToString("yyyy-MM-ddTHH:mm:ssZ")
$jsonBody = @{
    appointmentUuid = "auth3-$(Get-Random)"
    patientUuid = "p1"
    patientName = "Auth Test"
    patientPhone = "+31610000002"
    patientEmail = "auth3@example.com"
    startDateTime = $startFar
    status = "Confirmed"
    location = "Loc"
    instructions = "Inst"
} | ConvertTo-Json -Compress
try {
    $auth3 = Invoke-JsonPost -Url "$ProducerBase/api/appointments" -Body $jsonBody -Headers @{
        "X-Api-Key" = $ApiKey
        "X-Organization-Key" = "default"
    }
    Write-TestResult -Id "AUTH3" -Ok ($auth3.StatusCode -eq 202) -Detail "header org key ingest => $($auth3.StatusCode)"
} catch {
    Write-TestResult -Id "AUTH3" -Ok $false -Detail $_.Exception.Message
}

# AUTH5 - org resolution: route wins over body
try {
    $auth5 = Invoke-JsonPost -Url "$ProducerBase/api/appointments/default" -Body (@{
        appointmentUuid = "auth5-$(Get-Random)"
        organizationKey = "other-org"
        patientUuid = "p1"
        patientName = "Auth5"
        patientPhone = "+31610000004"
        patientEmail = "a5@example.com"
        startDateTime = $startFar
        status = "Confirmed"
        location = "L"
        instructions = "I"
    } | ConvertTo-Json -Compress) -Headers @{ "X-Api-Key" = $ApiKey }
    Write-TestResult -Id "AUTH5" -Ok ($auth5.StatusCode -eq 202) -Detail "route org default => $($auth5.StatusCode)"
} catch {
    Write-TestResult -Id "AUTH5" -Ok $false -Detail $_.Exception.Message
}

# E15 empty body
try {
    Invoke-JsonPost -Url "$ProducerBase/api/appointments/default" -Body "" -Headers @{ "X-Api-Key" = $ApiKey }
    Write-TestResult -Id "E15" -Ok $false -Detail "expected 400"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-TestResult -Id "E15" -Ok ($code -eq 400) -Detail "empty body => $code"
}

# E7 invalid FHIR JSON
$fhirBad = '{"resourceType":"Patient"}'
try {
    Invoke-JsonPost -Url "$ProducerBase/fhir/Appointment/default" -Body $fhirBad -Headers @{
        "X-Api-Key" = $ApiKey
        Accept = "application/fhir+json"
    } -ContentType "application/fhir+json"
    Write-TestResult -Id "E8" -Ok $false -Detail "expected 400 for wrong resourceType"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-TestResult -Id "E8" -Ok ($code -eq 400) -Detail "wrong resourceType => $code"
}

# E3/E12 FHIR appointment ~70 min ahead (catch-up 24h)
$startSoon = (Get-Date).ToUniversalTime().AddMinutes(70).ToString("yyyy-MM-ddTHH:mm:ssZ")
$uuid = "comprehensive-$(Get-Date -Format 'yyyyMMddHHmmss')"
$fhirBody = @"
{
  "resourceType": "Appointment",
  "status": "booked",
  "start": "$startSoon",
  "identifier": [{ "system": "http://openmrs.org/appointment", "value": "$uuid" }],
  "participant": [{
    "actor": { "reference": "Patient/p1", "display": "Comprehensive Test" },
    "status": "accepted"
  }],
  "patientInstruction": "Test instructions",
  "extension": [
    { "url": "http://notification-module.local/StructureDefinition/patient-phone", "valueString": "+31612345678" },
    { "url": "http://notification-module.local/StructureDefinition/patient-email", "valueString": "comprehensive@example.com" },
    { "url": "http://notification-module.local/StructureDefinition/location-text", "valueString": "Test location" }
  ]
}
"@
$fhirResp = Invoke-JsonPost -Url "$ProducerBase/fhir/Appointment/default" -Body $fhirBody -Headers @{
    "X-Api-Key" = $ApiKey
    Accept = "application/fhir+json"
} -ContentType "application/fhir+json"
Write-TestResult -Id "E3" -Ok (($fhirResp.StatusCode -eq 201) -or ($fhirResp.StatusCode -eq 200)) -Detail "FHIR POST => $($fhirResp.StatusCode)"

# E13 legacy JSON
$jsonAppt = @{
    appointmentUuid = "legacy-$uuid"
    organizationKey = "default"
    patientUuid = "p2"
    patientName = "Legacy Test"
    patientPhone = "+31610000003"
    patientEmail = "legacy@example.com"
    startDateTime = $startFar
    status = "Confirmed"
    location = "Loc"
    instructions = "Inst"
} | ConvertTo-Json -Compress
$legacy = Invoke-JsonPost -Url "$ProducerBase/api/appointments/default" -Body $jsonAppt -Headers @{ "X-Api-Key" = $ApiKey }
$legacyJson = $legacy.Content | ConvertFrom-Json
Write-TestResult -Id "E13" -Ok (($legacy.StatusCode -eq 202) -and ($legacyJson.pendingNotifications -eq 2)) -Detail "legacy POST pending=$($legacyJson.pendingNotifications)"

# E17 consumer 404
try {
    $c404 = Invoke-WebRequest -Uri "$ConsumerBase/api/appointments" -UseBasicParsing -TimeoutSec 5
    Write-TestResult -Id "E17" -Ok ($c404.StatusCode -eq 404) -Detail "consumer route => $($c404.StatusCode)"
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-TestResult -Id "E17" -Ok ($code -eq 404) -Detail "consumer unknown route => $code"
}

# --- Database DB4-DB18 ---
Write-Host "`n==> Database checks (DB4-DB18)"
$dbSql = @"
SELECT COUNT(*) FROM organizations WHERE "Key" = 'default';
SELECT COUNT(*) FROM organization_api_keys;
SELECT COUNT(*) FROM provider_secrets;
SELECT COUNT(*) FROM appointments WHERE "AppointmentUuid" = '$uuid';
SELECT COUNT(*) FROM scheduled_notifications sn
  JOIN appointments a ON a."Id" = sn."AppointmentId"
  WHERE a."AppointmentUuid" = '$uuid' AND sn."Status" = 'Pending';
"@
$dbOut = ($dbSql | docker exec -i notification-postgres psql -U notification -d notification -t -A 2>&1 | Out-String)
$lines = @($dbOut -split "`r?`n" | Where-Object { $_ -match '^\d+$' })
Write-TestResult -Id "DB4" -Ok (($lines.Count -ge 1) -and ([int]$lines[0] -ge 1)) -Detail "default org count=$($lines[0])"
Write-TestResult -Id "DB5" -Ok (($lines.Count -ge 2) -and ([int]$lines[1] -ge 1)) -Detail "api_keys count=$($lines[1])"
Write-TestResult -Id "DB6" -Ok (($lines.Count -ge 3) -and ([int]$lines[2] -ge 1)) -Detail "provider_secrets count=$($lines[2])"
Write-TestResult -Id "DB9" -Ok (($lines.Count -ge 4) -and ([int]$lines[3] -ge 1)) -Detail "appointments from ingest count=$($lines[3])"
Write-TestResult -Id "DB9b" -Ok (($lines.Count -ge 5) -and ([int]$lines[4] -ge 2)) -Detail "pending reminders for FHIR appt=$($lines[4])"

# SEC1 - no plaintext keys
$secOut = (@'
SELECT COUNT(*) FROM organization_api_keys WHERE "KeyHash" IS NULL OR length("KeyHash") < 10;
'@ | docker exec -i notification-postgres psql -U notification -d notification -t -A 2>&1 | Out-String).Trim()
Write-TestResult -Id "SEC1" -Ok ([int]$secOut -eq 0) -Detail "no invalid key hashes"

# --- MQ / pipeline: wait for consumer dispatch ---
Write-Host "`n==> Pipeline (MQ3, consumer logs)"
$dispatchSeen = $false
for ($i = 1; $i -le 24; $i++) {
    $logs = docker compose --env-file env.example logs consumer --tail 100 2>&1
    if ($logs -match "Sending via") {
        $dispatchSeen = $true
        break
    }
    Write-Host "  waiting for dispatch ($i/24)..."
    Start-Sleep -Seconds 30
}
Write-TestResult -Id "MQ3" -Ok $dispatchSeen -Detail "consumer log contains 'Sending via'"

# DB16 delivery after dispatch (poll for 24h catch-up)
if ($dispatchSeen) {
    $db16Ok = $false
    $deliveryCount = 0
    $sentOrQueued = 0
    for ($i = 1; $i -le 18; $i++) {
        $delSql = @"
SELECT COUNT(*) FROM notification_deliveries nd
JOIN scheduled_notifications sn ON nd."ScheduledNotificationId" = sn."Id"
JOIN appointments a ON a."Id" = sn."AppointmentId"
WHERE a."AppointmentUuid" = '$uuid';
SELECT COUNT(*) FROM scheduled_notifications sn
JOIN appointments a ON a."Id" = sn."AppointmentId"
WHERE a."AppointmentUuid" = '$uuid' AND sn."Status" IN ('Sent', 'Queued');
"@
        $delOut = ($delSql | docker exec -i notification-postgres psql -U notification -d notification -t -A 2>&1 | Out-String)
        $delLines = @($delOut -split "`r?`n" | Where-Object { $_ -match '^\d+$' })
        $deliveryCount = if ($delLines.Count -ge 1) { [int]$delLines[0] } else { 0 }
        $sentOrQueued = if ($delLines.Count -ge 2) { [int]$delLines[1] } else { 0 }
        if (($deliveryCount -gt 0) -or ($sentOrQueued -gt 0)) {
            $db16Ok = $true
            break
        }
        Write-Host "  DB16 wait ($i/18): deliveries=$deliveryCount sentOrQueued=$sentOrQueued"
        Start-Sleep -Seconds 10
    }
    Write-TestResult -Id "DB16" -Ok $db16Ok -Detail "deliveries=$deliveryCount sentOrQueued=$sentOrQueued"
}

# --- Observability metrics M1-M4 (subset via Prometheus) ---
if (-not $SkipMetricsWait) {
    Write-Host "`n==> Prometheus metrics (M1, M10-M13)"
    Wait-HttpStatus -Url "http://127.0.0.1:9090/-/ready" -AcceptCodes @(200) -MaxAttempts 30 | Out-Null
    $ingest = 0.0
    $dispatch = 0.0
    $delivery = 0.0
    $received = 0.0
    $maxAttempts = [math]::Max(1, $MetricsWaitMinutes * 4)
    for ($i = 1; $i -le $maxAttempts; $i++) {
        $ingest = Get-PromMaxValue -PromResult (Query-Prometheus "increase(appointments_ingested_total[15m])")
        $dispatch = Get-PromMaxValue -PromResult (Query-Prometheus "increase(notification_dispatch_dispatches_total[15m])")
        $delivery = Get-PromMaxValue -PromResult (Query-Prometheus "increase(notification_delivery_success_deliveries_total[15m])")
        $received = Get-PromMaxValue -PromResult (Query-Prometheus "increase(notification_messages_received_total[15m])")
        Write-Host "  metrics attempt ${i}: ingest=$ingest dispatch=$dispatch delivery=$delivery received=$received"
        if ($ingest -gt 0 -and $dispatch -gt 0 -and $delivery -gt 0 -and $received -gt 0) { break }
        Start-Sleep -Seconds 15
    }
    Write-TestResult -Id "M1" -Ok ($ingest -gt 0) -Detail "appointments_ingested increase=$ingest"
    Write-TestResult -Id "M10" -Ok ($dispatch -gt 0) -Detail "notification_dispatch increase=$dispatch"
    Write-TestResult -Id "M12" -Ok ($delivery -gt 0) -Detail "delivery_success increase=$delivery"
    Write-TestResult -Id "M9" -Ok ($received -gt 0) -Detail "messages_received increase=$received"

    # Prometheus rules loaded
    try {
        $rules = Invoke-WebRequest -Uri "http://127.0.0.1:9090/api/v1/rules" -UseBasicParsing
        $rulesOk = $rules.Content -match "NotificationDeliveryFailureSpike"
        Write-TestResult -Id "AL-rules" -Ok $rulesOk -Detail "alert rules loaded in Prometheus"
    } catch {
        Write-TestResult -Id "AL-rules" -Ok $false -Detail $_.Exception.Message
    }
}

# --- Jaeger T1 (services registered) ---
Write-Host "`n==> Jaeger (T1)"
try {
    $svc = Invoke-WebRequest -Uri "http://127.0.0.1:16686/api/services" -UseBasicParsing
    $svcOk = ($svc.Content -match "notification-producer") -and ($svc.Content -match "notification-consumer")
    Write-TestResult -Id "T1" -Ok $svcOk -Detail "Jaeger services list contains producer and consumer"
} catch {
    Write-TestResult -Id "T1" -Ok $false -Detail $_.Exception.Message
}

# --- Loki ready ---
Write-Host "`n==> Loki (L1 proxy)"
$lokiOk = $false
for ($i = 1; $i -le 10; $i++) {
    try {
        $loki = Invoke-WebRequest -Uri "http://127.0.0.1:3100/ready" -UseBasicParsing
        if ($loki.StatusCode -eq 200) { $lokiOk = $true; break }
    } catch { Start-Sleep -Seconds 3 }
}
Write-TestResult -Id "L1" -Ok $lokiOk -Detail "Loki /ready"

# --- SEC3 metadata public ---
Write-TestResult -Id "SEC3" -Ok $metaOk -Detail "/fhir/metadata accessible without API key"

# --- SEC2 encrypted secrets ---
$sec2Out = (@'
SELECT COUNT(*) FROM provider_secrets WHERE "EncryptedPayload" IS NULL OR "Nonce" IS NULL;
'@ | docker exec -i notification-postgres psql -U notification -d notification -t -A 2>&1 | Out-String).Trim()
Write-TestResult -Id "SEC2" -Ok ([int]$sec2Out -eq 0) -Detail "all provider secrets encrypted"

# --- Provider dispatch (P1 SwiftSend default) ---
$providerOk = $false
if ($dispatchSeen) {
    $plog = docker compose --env-file env.example logs consumer --tail 200 2>&1
    $providerOk = [bool]($plog -match "SwiftSend|Sending via")
}
Write-TestResult -Id "P1" -Ok $providerOk -Detail "default provider dispatch in logs"

# --- MQ1 queue exists ---
try {
    $queues = Invoke-RestMethod -Uri "http://127.0.0.1:15672/api/queues" -Headers @{
        Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest"))
    }
    $mq1 = ($queues | Where-Object { $_.name -match "notifications\." }).Count -ge 1
    Write-TestResult -Id "MQ1" -Ok $mq1 -Detail "appointment queue present in RabbitMQ"
} catch {
    Write-TestResult -Id "MQ1" -Ok $false -Detail $_.Exception.Message
}

# --- Grafana (G1) ---
try {
    $gf = Invoke-WebRequest -Uri "http://127.0.0.1:3000/api/health" -UseBasicParsing
    Write-TestResult -Id "G1" -Ok ($gf.StatusCode -eq 200) -Detail "Grafana health API"
} catch {
    Write-TestResult -Id "G1" -Ok $false -Detail $_.Exception.Message
}

Write-Host "`n==> Summary: PASS=$($script:Passed) FAIL=$($script:Failed) SKIP=$($script:Skipped)"
$script:Results | ForEach-Object { Write-Host $_ }

if ($script:Failed -gt 0) { exit 1 }
exit 0
