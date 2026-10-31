# Run in your PowerShell session.
# Seeds an active showtime (starting in ~15 mins) and issues a valid paid demo ticket for immediate staff check-in scanning.
# Note: Raw QR token is masked by default. Set $env:BGP_SHOW_DEMO_QR_TOKEN = "true" if you explicitly need the raw plaintext token for debugging.
$ErrorActionPreference = 'Stop'

# Prepare session environment (connection string, keys)
. (Join-Path $PSScriptRoot "01_prepare_dev_session.ps1")

$runnerPath = Join-Path $PSScriptRoot "..\src\BetaGiaiPhong.Runner"
Write-Host "Running BetaGiaiPhong.Runner seed-checkin-demo..." -ForegroundColor Cyan
dotnet run --project $runnerPath -- seed-checkin-demo
