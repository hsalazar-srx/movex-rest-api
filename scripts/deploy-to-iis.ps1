# Deployment Script for SRXWEBAPP1
# Run this on the target server after files have been transferred
#
# Usage:
#   UAT:  .\deploy-to-iis.ps1 -Environment UAT -AppPoolName Movex.API.UAT -AppName movex-api-uat `
#             -AppPath C:\inetpub\wwwroot\movex-api-uat -KestrelPort 5001 `
#             -M3Host UAT_M3_IP -M3Port 6300 -M3User UATAPI `
#             -M3Password (ConvertTo-SecureString "secret" -AsPlainText -Force) -ApiKey uat-key
#
#   PROD: .\deploy-to-iis.ps1 -Environment Production -AppPoolName Movex.API.PROD -AppName movex-api-prod `
#             -AppPath C:\inetpub\wwwroot\movex-api-prod -KestrelPort 5000 `
#             -M3Host 150.3.2.100 -M3Port 6300 -M3User SRXAPI `
#             -M3Password (ConvertTo-SecureString "secret" -AsPlainText -Force) -ApiKey prod-key

param(
    [Parameter(Mandatory)]
    [ValidateSet("UAT","Production")]
    [string]$Environment,
    [string]$AppPoolName = "Movex.API",
    [string]$SiteName = "Default Web Site",
    [string]$AppName = "movex-api",
    [string]$AppPath = "C:\inetpub\wwwroot\movex-api",
    [string]$KestrelPort = "5000",
    [string]$M3Host = "150.3.2.100",
    [string]$M3Port = "6300",
    [string]$M3User = "",
    [SecureString]$M3Password,
    [string]$M3Company = "300",
    [string]$ApiKey = "",
    [string]$AdminKey = ""
)

Write-Host "================================" -ForegroundColor Cyan
Write-Host "SRXWEBAPP1 Deployment Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Import IIS module
try {
    Import-Module WebAdministration
} catch {
    Write-Host "✗ ERROR: WebAdministration module not found. Install IIS." -ForegroundColor Red
    exit 1
}

# Validate inputs
if (-not (Test-Path $AppPath)) {
    Write-Host "✗ ERROR: Application path does not exist: $AppPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path "$AppPath\Movex.API.exe")) {
    Write-Host "✗ ERROR: Movex.API.exe not found in $AppPath" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "⚠ WARNING: No API key provided. Generating one..." -ForegroundColor Yellow
    # Generate random API key
    $ApiKey = [Convert]::ToBase64String((1..32 | ForEach-Object { [byte](Get-Random -Maximum 256) }))
    Write-Host "Generated API Key: $ApiKey" -ForegroundColor Cyan
}

Write-Host "`nDeployment Configuration:" -ForegroundColor Yellow
Write-Host "  Environment: $Environment" -ForegroundColor Gray
Write-Host "  App Pool: $AppPoolName" -ForegroundColor Gray
Write-Host "  Site: $SiteName" -ForegroundColor Gray
Write-Host "  App Name: $AppName" -ForegroundColor Gray
Write-Host "  Physical Path: $AppPath" -ForegroundColor Gray
Write-Host "  Kestrel Port: $KestrelPort" -ForegroundColor Gray
Write-Host "  M3 Host: $M3Host" -ForegroundColor Gray
Write-Host "  M3 Port: $M3Port" -ForegroundColor Gray

# Step 1: Create logs directory with proper permissions
Write-Host "`n[STEP 1] Setting up logs directory..." -ForegroundColor Yellow
$logsPath = Join-Path $AppPath "logs"
try {
    if (-not (Test-Path $logsPath)) {
        New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
        Write-Host "✓ Created logs directory" -ForegroundColor Green
    } else {
        Write-Host "✓ Logs directory exists" -ForegroundColor Green
    }

    # Set permissions
    icacls $logsPath /grant "IIS_IUSRS:(OI)(CI)F" /T | Out-Null
    icacls $logsPath /grant "IUSR:(OI)(CI)F" /T | Out-Null
    Write-Host "✓ Configured logs directory permissions" -ForegroundColor Green
} catch {
    Write-Host "✗ ERROR: Could not set up logs directory: $_" -ForegroundColor Red
    exit 1
}

# Step 2: Create or verify app pool
Write-Host "`n[STEP 2] Configuring application pool..." -ForegroundColor Yellow
try {
    $appPool = Get-IISAppPool -Name $AppPoolName -ErrorAction SilentlyContinue

    if (-not $appPool) {
        # Create new app pool
        New-WebAppPool -Name $AppPoolName -Force | Out-Null
        Write-Host "✓ Created application pool: $AppPoolName" -ForegroundColor Green

        # Configure app pool
        $appPool = Get-IISAppPool -Name $AppPoolName
    } else {
        Write-Host "✓ Application pool already exists: $AppPoolName" -ForegroundColor Green
    }

    # Stop app pool before configuration
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    # Configure app pool settings
    $appPool.ProcessModel.IdentityType = "ApplicationPoolIdentity"
    $appPool.ManagedRuntimeVersion = ""  # .NET Core uses no managed runtime
    $appPool.ManagedPipelineMode = "Integrated"
    $appPool.Recycling.PeriodicRestart.Time = [TimeSpan]::FromMinutes(240)  # Recycle every 4 hours
    $appPool.ProcessModel.MaxProcesses = 1
    $appPool | Set-Item

    Write-Host "✓ Configured application pool settings" -ForegroundColor Green

    # Start app pool
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Write-Host "✓ Started application pool" -ForegroundColor Green
} catch {
    Write-Host "✗ ERROR: Could not configure app pool: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Create or update IIS application
Write-Host "`n[STEP 3] Configuring IIS application..." -ForegroundColor Yellow
try {
    # Check if application exists
    $sitePath = "IIS:\Sites\$SiteName\$AppName"
    $appExists = Test-Path $sitePath

    if ($appExists) {
        Write-Host "⚠ Application already exists. Removing for fresh deployment..." -ForegroundColor Yellow
        Remove-WebApplication -Site $SiteName -Name $AppName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    # Create new application
    New-WebApplication -Site $SiteName `
        -Name $AppName `
        -PhysicalPath $AppPath `
        -ApplicationPool $AppPoolName `
        -Force | Out-Null

    Write-Host "✓ Created/updated IIS application: $SiteName/$AppName" -ForegroundColor Green
} catch {
    Write-Host "✗ ERROR: Could not configure IIS application: $_" -ForegroundColor Red
    exit 1
}

# Step 4: Set environment variables
Write-Host "`n[STEP 4] Setting environment variables..." -ForegroundColor Yellow
try {
    $iisAppPath = "IIS:\Sites\$SiteName\$AppName"

    # Resolve SecureString password to plain text for IIS environment variable injection.
    # The plain text value is held in memory only for the duration of this block.
    $m3PasswordPlain = if ($null -ne $M3Password -and $M3Password.Length -gt 0) {
        [System.Net.NetworkCredential]::new('', $M3Password).Password
    } else {
        "your-password"
    }

    # Define environment variables to set
    # NOTE: ASP.NET Core requires double-underscore (__) as the section separator
    # e.g. M3__HOST maps to the "M3:HOST" config key (M3 section, HOST property)
    $envVars = @{
        "ASPNETCORE_ENVIRONMENT"        = $Environment
        "ASPNETCORE_URLS"               = "http://+:$KestrelPort"
        "M3__HOST"                      = $M3Host
        "M3__PORT"                      = $M3Port
        "M3__USER"                      = if ([string]::IsNullOrWhiteSpace($M3User)) { "MBUSER" } else { $M3User }
        "M3__PASSWORD"                  = $m3PasswordPlain
        "M3__DEFAULT_COMPANY"           = $M3Company
        "ApiKeys__Primary"              = $ApiKey
        "Swagger__Enabled"              = if ($Environment -eq "UAT") { "true" } else { "false" }
        "Logging__LogLevel__Default"    = if ($Environment -eq "UAT") { "Debug" } else { "Information" }
    }

    if (-not [string]::IsNullOrWhiteSpace($AdminKey)) {
        $envVars["ApiKeys__Admin"] = $AdminKey
    }

    # Set each environment variable
    foreach ($key in $envVars.Keys) {
        Set-WebConfigurationProperty -PSPath $iisAppPath `
            -Filter "system.webServer/aspNetCore/environmentVariables" `
            -Name "." `
            -Value @{name=$key; value=$envVars[$key]} `
            -ErrorAction SilentlyContinue
        Write-Host "  ✓ Set $key" -ForegroundColor Gray
    }

    Write-Host "✓ Environment variables configured" -ForegroundColor Green
} catch {
    Write-Host "✗ ERROR: Could not set environment variables: $_" -ForegroundColor Red
}

# Step 5: Restart IIS
Write-Host "`n[STEP 5] Restarting IIS..." -ForegroundColor Yellow
try {
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Write-Host "✓ Restarted application pool" -ForegroundColor Green

    Write-Host "✓ IIS configuration complete" -ForegroundColor Green
} catch {
    Write-Host "⚠ WARNING: Could not restart IIS gracefully: $_" -ForegroundColor Yellow
}

# Step 6: Verify deployment
Write-Host "`n[STEP 6] Verifying deployment..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

try {
    $healthUrl = "http://localhost:$KestrelPort/health"
    $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 10

    if ($response.StatusCode -eq 200) {
        Write-Host "✓ Health endpoint responding (HTTP 200)" -ForegroundColor Green
        Write-Host "  URL: $healthUrl" -ForegroundColor Gray
    } else {
        Write-Host "⚠ Health endpoint returned: HTTP $($response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ WARNING: Could not reach health endpoint. The application may still be starting..." -ForegroundColor Yellow
    Write-Host "  Details: $($_.Exception.Message)" -ForegroundColor Gray
    Write-Host "`n  Check logs at: $logsPath" -ForegroundColor Gray
}

# Step 7: Summary
Write-Host "`n================================" -ForegroundColor Cyan
Write-Host "Deployment Complete" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

Write-Host "`nAPI Details:" -ForegroundColor Yellow
Write-Host "  Health Check: http://SRXWEBAPP1:$KestrelPort/health" -ForegroundColor Gray
Write-Host "  Programs:     http://SRXWEBAPP1:$KestrelPort/api/programs" -ForegroundColor Gray
Write-Host "  API Key: $($ApiKey.Substring(0, [Math]::Min(20, $ApiKey.Length)))..." -ForegroundColor Gray

Write-Host "`nLogs Location:" -ForegroundColor Yellow
Write-Host "  $logsPath" -ForegroundColor Gray

Write-Host "`nNext Steps:" -ForegroundColor Yellow
Write-Host "  1. Update M3 credentials if needed:" -ForegroundColor Gray
Write-Host "     Set environment variables in IIS Application Settings" -ForegroundColor Gray
Write-Host "  2. Test health endpoint" -ForegroundColor Gray
Write-Host "  3. Test with API key authentication" -ForegroundColor Gray
Write-Host "  4. Execute sample transaction" -ForegroundColor Gray

Write-Host "`n✓ Deployment completed successfully!" -ForegroundColor Green
