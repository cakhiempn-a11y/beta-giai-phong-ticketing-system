# Run in your own PowerShell session. Never commit real passwords to git.
# This script sets the app connection string and synchronizes development keys for the current shell.
$ErrorActionPreference = 'Stop'

# Database Connection Configuration:
# 1. Respect existing ConnectionStrings__BetaDb if already configured in environment
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__BetaDb)) {
    if (-not [string]::IsNullOrWhiteSpace($env:BGP_SQL_SA_PASSWORD)) {
        # Docker SQL Server (Port 14333, SA account)
        $env:ConnectionStrings__BetaDb = "Server=127.0.0.1,14333;Database=BetaGiaiPhongDev;User Id=sa;Password=$($env:BGP_SQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
        Write-Host "Configured ConnectionStrings__BetaDb for Docker SQL Server (127.0.0.1,14333)."
    } else {
        # Default to local Windows SQL Server (Integrated Security)
        $env:ConnectionStrings__BetaDb = "Server=localhost;Database=BetaGiaiPhong_Dev;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
        Write-Host "Configured default ConnectionStrings__BetaDb for local Windows SQL Server (localhost)."
    }
} else {
    Write-Host "Preserving existing ConnectionStrings__BetaDb from environment."
}

$env:BGP_ALLOW_DEMO_SEED = 'true'
if ([string]::IsNullOrWhiteSpace($env:BGP_DEMO_PASSWORD)) {
    Write-Warning 'Set BGP_DEMO_PASSWORD with >= 12 characters before running seed-demo.'
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

# 1. Attempt to load stable keys from .NET User Secrets (src/BetaGiaiPhong.Web) to keep keys stable across sessions
$keysFromSecrets = $false
if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64) -or [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
    try {
        $webProjectPath = Join-Path $PSScriptRoot "..\src\BetaGiaiPhong.Web"
        $secretsOutput = dotnet user-secrets list --project $webProjectPath 2>$null
        foreach ($line in $secretsOutput) {
            if ($line -match '^\s*BGP_MOCK_HMAC_KEY_B64\s*=\s*(.+)$' -and [string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64)) {
                $env:BGP_MOCK_HMAC_KEY_B64 = $matches[1].Trim()
                Write-Host 'Synchronized stable BGP_MOCK_HMAC_KEY_B64 from .NET User Secrets.'
            }
            if ($line -match '^\s*BGP_QR_AES_KEY_B64\s*=\s*(.+)$' -and [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
                $env:BGP_QR_AES_KEY_B64 = $matches[1].Trim()
                Write-Host 'Synchronized stable BGP_QR_AES_KEY_B64 from .NET User Secrets.'
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64) -and -not [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
            $keysFromSecrets = $true
        }
    } catch {
        # Fall back if dotnet CLI or project secrets are not available
    }
} else {
    $keysFromSecrets = $true
}

# 2. If keys are missing, explicitly warn and instruct developer on setting up .NET User Secrets
if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64) -or [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
    Write-Warning @"
Missing stable cryptographic keys in .NET User Secrets!
To make keys persist across development sessions, run these commands:
    `$hmac = [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Minimum 0 -Maximum 256) }))
    `$aes  = [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Minimum 0 -Maximum 256) }))
    dotnet user-secrets set "BGP_MOCK_HMAC_KEY_B64" "`$hmac" --project .\src\BetaGiaiPhong.Web
    dotnet user-secrets set "BGP_QR_AES_KEY_B64" "`$aes" --project .\src\BetaGiaiPhong.Web
"@

    # Fallback to ephemeral keys for the current shell only
    if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64)) {
        $env:BGP_MOCK_HMAC_KEY_B64 = New-SecureRandomBase64Key 32
        Write-Warning 'Generated ephemeral BGP_MOCK_HMAC_KEY_B64 for current shell process.'
    }
    if ([string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
        $env:BGP_QR_AES_KEY_B64 = New-SecureRandomBase64Key 32
        Write-Warning 'Generated ephemeral BGP_QR_AES_KEY_B64 for current shell process.'
    }
    Write-Warning 'CAUTION: Session is using EPHEMERAL keys. Previously issued QR codes or webhook signatures will become INVALID once this shell closes.'
} else {
    Write-Host 'Local environment configured. Keys are stable across restarts via .NET User Secrets.'
}

Write-Host 'App DB user is SA/Trusted for LOCAL DEV ONLY; create least-privilege credentials before non-local deployments.'
