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
$env:BGP_ALLOW_CHECKIN_DEMO = 'true'
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
# Note: BetaGiaiPhong.Web automatically reads User Secrets in Development.
# However, CLI tools (like BetaGiaiPhong.Runner) require keys to be exported to environment variables.
if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64) -or [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
    try {
        $webProjectPath = Join-Path $PSScriptRoot "..\src\BetaGiaiPhong.Web"
        $secretsOutput = dotnet user-secrets list --project $webProjectPath 2>$null
        foreach ($line in $secretsOutput) {
            if ($line -match '^\s*BGP_MOCK_HMAC_KEY_B64\s*=\s*(.+)$' -and [string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64)) {
                $env:BGP_MOCK_HMAC_KEY_B64 = $matches[1].Trim()
                Write-Host 'Synchronized stable BGP_MOCK_HMAC_KEY_B64 from .NET User Secrets to environment.'
            }
            if ($line -match '^\s*BGP_QR_AES_KEY_B64\s*=\s*(.+)$' -and [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
                $env:BGP_QR_AES_KEY_B64 = $matches[1].Trim()
                Write-Host 'Synchronized stable BGP_QR_AES_KEY_B64 from .NET User Secrets to environment.'
            }
        }
    } catch {
        # Fall back if dotnet CLI or project secrets are not available
    }
}

# 2. Check whether stable keys are configured
if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64) -or [string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
    if ($env:BGP_ALLOW_EPHEMERAL_KEYS -eq 'true') {
        # Explicit opt-in for ephemeral keys in this session only
        if ([string]::IsNullOrWhiteSpace($env:BGP_MOCK_HMAC_KEY_B64)) {
            $env:BGP_MOCK_HMAC_KEY_B64 = New-SecureRandomBase64Key 32
        }
        if ([string]::IsNullOrWhiteSpace($env:BGP_QR_AES_KEY_B64)) {
            $env:BGP_QR_AES_KEY_B64 = New-SecureRandomBase64Key 32
        }
        Write-Warning 'CAUTION: Session is using EPHEMERAL keys ($env:BGP_ALLOW_EPHEMERAL_KEYS=true). Previously issued tickets or webhook signatures will be INVALID across sessions.'
    } else {
        # Fail-closed: do not automatically generate ephemeral keys
        $setupInstructions = @"
[ERROR] Missing cryptographic keys in .NET User Secrets and environment!
To generate stable 256-bit CSPRNG keys, run the following commands once:

    `$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    `$hmacBytes = New-Object byte[] 32; `$rng.GetBytes(`$hmacBytes)
    `$aesBytes = New-Object byte[] 32; `$rng.GetBytes(`$aesBytes)
    `$hmac = [Convert]::ToBase64String(`$hmacBytes)
    `$aes  = [Convert]::ToBase64String(`$aesBytes)
    dotnet user-secrets set "BGP_MOCK_HMAC_KEY_B64" "`$hmac" --project .\src\BetaGiaiPhong.Web
    dotnet user-secrets set "BGP_QR_AES_KEY_B64" "`$aes" --project .\src\BetaGiaiPhong.Web
    Remove-Variable hmacBytes, aesBytes, hmac, aes -ErrorAction SilentlyContinue

IMPORTANT:
- NEVER rotate BGP_QR_AES_KEY_B64 if the database already contains issued tickets, as existing QR codes cannot be verified without the original key.
- Web project uses User Secrets in Development, while Runner reads environment variables synchronized by this script.
- To temporarily allow ephemeral keys for a single shell session, set `$env:BGP_ALLOW_EPHEMERAL_KEYS="true" before running this script.
"@
        Write-Error $setupInstructions
        throw "Cryptographic keys are not configured. Run the setup instructions above or set `$env:BGP_ALLOW_EPHEMERAL_KEYS='true'."
    }
} else {
    Write-Host 'Local environment configured. Cryptographic keys are synchronized for this session.'
}

Write-Host 'App DB user is SA/Trusted for LOCAL DEV ONLY; create least-privilege credentials before non-local deployments.'
