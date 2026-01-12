# Pre-Deployment Verification Script for SRXWEBAPP1
# Run this script on the target server before deployment

Write-Host "================================" -ForegroundColor Cyan
Write-Host "SRXWEBAPP1 Pre-Deployment Check" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

$errorCount = 0
$warningCount = 0

# Test 1: .NET 8.0 Runtime
Write-Host "`n[TEST 1] Checking .NET 8.0 Runtime..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    if ($dotnetVersion -like "8.0*" -or $dotnetVersion -like "9.0*") {
        Write-Host "✓ .NET Runtime installed: $dotnetVersion" -ForegroundColor Green
    } else {
        Write-Host "✗ ERROR: .NET 8.0 not found. Installed: $dotnetVersion" -ForegroundColor Red
        $errorCount++
    }
} catch {
    Write-Host "✗ ERROR: dotnet command not found. Install .NET 8.0 Runtime" -ForegroundColor Red
    $errorCount++
}

# Test 2: IIS Installation
Write-Host "`n[TEST 2] Checking IIS Installation..." -ForegroundColor Yellow
try {
    $iisFeature = Get-WindowsFeature -Name "Web-Server" -ErrorAction SilentlyContinue
    if ($iisFeature.Installed) {
        Write-Host "✓ IIS is installed" -ForegroundColor Green
    } else {
        Write-Host "✗ ERROR: IIS not installed. Install Web-Server role" -ForegroundColor Red
        $errorCount++
    }
} catch {
    Write-Host "✗ ERROR: Could not check IIS status" -ForegroundColor Red
    $errorCount++
}

# Test 3: ASP.NET Core Hosting Bundle
Write-Host "`n[TEST 3] Checking ASP.NET Core Hosting Bundle..." -ForegroundColor Yellow
try {
    $hostingModules = Get-Module -ListAvailable -Name "*AspNetCoreModule*" -ErrorAction SilentlyContinue
    $iisModules = Get-IISModule -Name "*AspNetCoreModule*" -ErrorAction SilentlyContinue
    
    if ($iisModules) {
        Write-Host "✓ ASP.NET Core Hosting Bundle is installed" -ForegroundColor Green
    } else {
        Write-Host "⚠ WARNING: ASP.NET Core Hosting Bundle not detected. May need to be installed/restarted" -ForegroundColor Yellow
        $warningCount++
    }
} catch {
    Write-Host "⚠ WARNING: Could not verify Hosting Bundle (may still work)" -ForegroundColor Yellow
    $warningCount++
}

# Test 4: M3 Server Connectivity
Write-Host "`n[TEST 4] Checking M3 Server Connectivity..." -ForegroundColor Yellow
$m3Host = "150.3.2.100"
$m3Port = 6000

try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $tcpClient.SendTimeout = 5000
    $tcpClient.ReceiveTimeout = 5000
    
    $result = $tcpClient.BeginConnect($m3Host, $m3Port, $null, $null)
    $result.AsyncWaitHandle.WaitOne(5000, $true) | Out-Null
    
    if ($tcpClient.Connected) {
        Write-Host "✓ Can reach M3 server at ${m3Host}:${m3Port}" -ForegroundColor Green
        $tcpClient.Close()
    } else {
        Write-Host "✗ ERROR: Cannot reach M3 server at ${m3Host}:${m3Port}. Check network and firewall" -ForegroundColor Red
        $errorCount++
    }
} catch {
    Write-Host "✗ ERROR: Cannot reach M3 server at ${m3Host}:${m3Port}" -ForegroundColor Red
    Write-Host "  Details: $($_.Exception.Message)" -ForegroundColor Red
    $errorCount++
}

# Test 5: Disk Space
Write-Host "`n[TEST 5] Checking Disk Space..." -ForegroundColor Yellow
try {
    $diskSpace = Get-PSDrive C | Select-Object @{Name="FreeGB"; Expression={[math]::Round($_.Free / 1GB, 2)}}
    if ($diskSpace.FreeGB -gt 1) {
        Write-Host "✓ Sufficient disk space available: $($diskSpace.FreeGB) GB free" -ForegroundColor Green
    } else {
        Write-Host "✗ ERROR: Insufficient disk space: $($diskSpace.FreeGB) GB free" -ForegroundColor Red
        $errorCount++
    }
} catch {
    Write-Host "⚠ WARNING: Could not check disk space" -ForegroundColor Yellow
    $warningCount++
}

# Test 6: Application Directory Structure
Write-Host "`n[TEST 6] Checking Application Directory..." -ForegroundColor Yellow
$appPath = "C:\inetpub\wwwroot\movex-api"
if (Test-Path $appPath) {
    Write-Host "✓ Application directory exists: $appPath" -ForegroundColor Green
    
    # Check for required files
    $requiredFiles = @("Movex.API.exe", "appsettings.json")
    foreach ($file in $requiredFiles) {
        if (Test-Path "$appPath\$file") {
            Write-Host "  ✓ Found: $file" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Missing: $file" -ForegroundColor Red
            $errorCount++
        }
    }
} else {
    Write-Host "⚠ WARNING: Application directory does not exist yet (will be created during deployment)" -ForegroundColor Yellow
}

# Test 7: Logs Directory
Write-Host "`n[TEST 7] Checking Logs Directory..." -ForegroundColor Yellow
$logsPath = "$appPath\logs"
if (Test-Path $logsPath) {
    Write-Host "✓ Logs directory exists: $logsPath" -ForegroundColor Green
    
    # Check permissions
    try {
        $acl = Get-Acl $logsPath
        $hasWriteAccess = $acl.Access | Where-Object { $_.IdentityReference -match "IIS AppPool" -and $_.FileSystemRights -match "Write" }
        if ($hasWriteAccess) {
            Write-Host "  ✓ IIS AppPool has write permissions" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ WARNING: May need to adjust permissions for IIS AppPool" -ForegroundColor Yellow
            $warningCount++
        }
    } catch {
        Write-Host "  ⚠ WARNING: Could not verify permissions" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠ WARNING: Logs directory will be created automatically" -ForegroundColor Yellow
}

# Test 8: IIS Application Pool
Write-Host "`n[TEST 8] Checking IIS Application Pool..." -ForegroundColor Yellow
try {
    $appPool = Get-IISAppPool -Name "Movex.API" -ErrorAction SilentlyContinue
    if ($appPool) {
        Write-Host "✓ Application pool 'Movex.API' exists" -ForegroundColor Green
        Write-Host "  State: $($appPool.State)" -ForegroundColor Gray
    } else {
        Write-Host "⚠ WARNING: Application pool 'Movex.API' not found (will be created during deployment)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ WARNING: Could not check application pool" -ForegroundColor Yellow
}

# Summary
Write-Host "`n================================" -ForegroundColor Cyan
Write-Host "Pre-Deployment Check Summary" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

if ($errorCount -eq 0) {
    Write-Host "✓ All critical checks passed!" -ForegroundColor Green
    if ($warningCount -gt 0) {
        Write-Host "⚠ $warningCount warnings (review above)" -ForegroundColor Yellow
    }
    Write-Host "`nYou can proceed with deployment." -ForegroundColor Green
    exit 0
} else {
    Write-Host "✗ $errorCount critical errors found" -ForegroundColor Red
    Write-Host "`nFix errors before proceeding with deployment." -ForegroundColor Red
    exit 1
}
