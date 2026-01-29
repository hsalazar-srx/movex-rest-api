<#
Generates a secure API key and stores it in .NET User Secrets.
Usage examples:
  ./scripts/generate-api-key.ps1                 # Base64, 32 bytes, store under ApiKeys:Primary
  ./scripts/generate-api-key.ps1 -Format Hex     # Hex, 32 bytes
  ./scripts/generate-api-key.ps1 -ShowOnly       # Generate only, do not store
#>
[CmdletBinding()] param(
    [ValidateSet('Base64','Hex')]
    [string]$Format = 'Base64',
    [int]$Bytes = 32,
    [string]$SecretName = 'ApiKeys:Primary',
    [string]$ProjectPath,
    [switch]$ShowOnly,
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ProjectPath) {
    # Default to the API project next to this script folder
    $ProjectPath = Join-Path (Join-Path $PSScriptRoot '..') 'Movex.API.csproj'
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    Write-Error "Project file not found: $ProjectPath. Provide -ProjectPath to the .csproj that has a <UserSecretsId>."
    exit 1
}

# Generate cryptographically secure random bytes
$randomBytes = New-Object byte[] $Bytes
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($randomBytes)

# Convert to requested format
switch ($Format) {
    'Base64' { $key = [Convert]::ToBase64String($randomBytes) }
    'Hex'    { $key = -join ($randomBytes | ForEach-Object { $_.ToString('x2') }) }
}

if ($ShowOnly) {
    Write-Host "Generated API key ($Format, $Bytes bytes):" -ForegroundColor Cyan
    Write-Host $key
    exit 0
}

# Store in User Secrets
Write-Host "Setting secret '$SecretName' for project: $ProjectPath" -ForegroundColor Cyan
$setCmd = "dotnet user-secrets set `"$SecretName`" `"$key`" --project `"$ProjectPath`""

# Use Start-Process for better error handling on quoting
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'dotnet'
$psi.Arguments = "user-secrets set `"$SecretName`" `"$key`" --project `"$ProjectPath`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$stdout = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()

if ($proc.ExitCode -ne 0) {
    Write-Error "Failed to set secret. ExitCode=$($proc.ExitCode). Error: $stderr"
    exit $proc.ExitCode
}

Write-Host "Secret stored successfully under '$SecretName'." -ForegroundColor Green
Write-Host "Key value:" -ForegroundColor DarkGray
Write-Host $key

if ($Verify) {
    Write-Host "Listing secrets for verification..." -ForegroundColor Cyan
    dotnet user-secrets list --project "$ProjectPath"
}

Write-Host "Use this header in requests: X-API-Key: $key" -ForegroundColor Yellow
