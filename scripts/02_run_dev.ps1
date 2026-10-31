$ErrorActionPreference = 'Stop'
# Run from repo root after calling .\scripts\01_prepare_dev_session.ps1 in SAME SESSION.
if (-not $env:ConnectionStrings__BetaDb) { throw 'ConnectionStrings__BetaDb missing.' }

dotnet restore .\BetaGiaiPhong.slnx
if ($LASTEXITCODE -ne 0) { throw 'restore failed' }
dotnet build .\BetaGiaiPhong.slnx --no-restore
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

foreach ($command in @('migrate', 'seed-demo', 'status', 'smoke', 'race', 'late', 'verify')) {
    Write-Host "`n== $command =="
    dotnet run --project .\src\BetaGiaiPhong.Runner -- $command
    if ($LASTEXITCODE -ne 0) { throw "$command failed" }
}
Write-Host 'All LOCAL checks passed.'
