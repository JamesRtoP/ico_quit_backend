<#
.SYNOPSIS
  Loads SYNTHETIC seed data into the local FHIR server (idempotent: PUTs with fixed ids).
.EXAMPLE
  ./scripts/seed.ps1
  ./scripts/seed.ps1 -FhirUrl http://localhost:8080/fhir
.NOTES
  Test users (pass as oid to `dotnet user-jwts create --claim oid=<oid>`):
    Patient A  medapp-seed-a  oid 11111111-1111-4111-8111-111111111111
    Patient B  medapp-seed-b  oid 22222222-2222-4222-8222-222222222222
    Unlinked   (no Patient)   oid 33333333-3333-4333-8333-333333333333  -> 403
#>
param([string]$FhirUrl = $(if ($env:FHIR_URL) { $env:FHIR_URL } else { 'http://localhost:8080/fhir' }))
$ErrorActionPreference = 'Stop'

if ($FhirUrl -notmatch '^http://(localhost|127\.0\.0\.1)([:/]|$)') {
    throw "Refusing to seed a non-local FHIR server: $FhirUrl"
}

$bundle = Join-Path $PSScriptRoot 'seed/seed-bundle.json'

Write-Host "Waiting for FHIR server at $FhirUrl ..."
$ready = $false
for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
    try { Invoke-WebRequest -UseBasicParsing "$FhirUrl/metadata" | Out-Null; $ready = $true }
    catch { Start-Sleep -Seconds 3 }
}
if (-not $ready) { throw 'FHIR server not reachable' }

Write-Host 'Posting seed transaction bundle ...'
Invoke-WebRequest -UseBasicParsing -Method Post -Uri $FhirUrl `
    -ContentType 'application/fhir+json' -InFile $bundle | Out-Null
Write-Host 'Seed data loaded.'
