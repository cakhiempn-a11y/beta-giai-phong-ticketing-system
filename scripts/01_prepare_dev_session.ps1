# Run in your own PowerShell session. Never commit real passwords to git.
# Set a STRONG, UNIQUE password manually or through a secret manager:
#   $env:BGP_SQL_SA_PASSWORD = '<your strong unique SA password>'
#   docker compose up -d
# This script sets the app connection string and generates ephemeral keys for the current shell.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:BGP_SQL_SA_PASSWORD)) {
    throw 'Set BGP_SQL_SA_PASSWORD in this PowerShell session first.'
}
$env:ConnectionStrings__BetaDb = "Server=127.0.0.1,14333;Database=BetaGiaiPhongDev;User Id=sa;Password=$($env:BGP_SQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
$env:BGP_ALLOW_DEMO_SEED = 'true'
if ([string]::IsNullOrWhiteSpace($env:BGP_DEMO_PASSWORD)) {
    Write-Warning 'Set BGP_DEMO_PASSWORD with >= 12 characters before seed-demo.'
}
function New-SecureRandomBase64Key([int]$length = 32) {
    $bytes = New-Object byte[] $length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        if ($rng -is [System.IDisposable]) {
            $rng.Dispose()
        }
    }
    return [Convert]::ToBase64String($bytes)
}

if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64)) {
    $env:BGP_MOCK_HMAC_KEY_B64 = New-SecureRandomBase64Key 32
}
if ([string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
    $env:BGP_QR_AES_KEY_B64 = New-SecureRandomBase64Key 32
}
Write-Host 'Local environment configured. Keep both keys stable across sessions if tickets must remain readable.'
Write-Host 'App DB user is SA for LOCAL DEV ONLY; create least-privilege credentials before non-local deployments.'
