param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
Set-Location $projectRoot

$env:SWAGGER_GENERATION = "true"
if (-not $env:ASPNETCORE_ENVIRONMENT) {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
}

Write-Host "[OpenAPI] Building project ($Configuration)..."
dotnet build -c $Configuration

$toolPath = Join-Path $projectRoot ".tools"
if (-not (Test-Path $toolPath)) {
    New-Item -ItemType Directory -Path $toolPath | Out-Null
}

$swaggerCliVersion = "6.9.0"

# Always uninstall first — avoids corrupt .tools state when version changes
$localTools = dotnet tool list --tool-path $toolPath 2>&1
if ($localTools -match "swashbuckle.aspnetcore.cli") {
    Write-Host "[OpenAPI] Removing existing Swashbuckle CLI..."
    dotnet tool uninstall --tool-path $toolPath Swashbuckle.AspNetCore.Cli | Out-Null
}

Write-Host "[OpenAPI] Installing Swashbuckle CLI $swaggerCliVersion (local)..."
dotnet tool install --tool-path $toolPath Swashbuckle.AspNetCore.Cli --version $swaggerCliVersion

$assemblyRoot = Join-Path $projectRoot "bin\$Configuration\net8.0"
$assemblyPath = Join-Path $assemblyRoot "Movex.API.dll"
if (-not (Test-Path $assemblyPath)) {
    $assemblyPath = Get-ChildItem -Path $assemblyRoot -Filter "Movex.API.dll" -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $assemblyPath) {
    throw "OpenAPI generation failed: assembly not found under $assemblyRoot"
}

$openApiPath = Join-Path $projectRoot "docs\openapi.json"
Write-Host "[OpenAPI] Generating spec to $openApiPath"
$swaggerExe = Join-Path $toolPath "swagger"
& $swaggerExe tofile --output $openApiPath $assemblyPath v1
if ($LASTEXITCODE -ne 0) {
    throw "OpenAPI generation failed: swagger command exited with code $LASTEXITCODE"
}

Write-Host "[OpenAPI] Done."
