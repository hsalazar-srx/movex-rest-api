# Troubleshooting Runbook - Movex.API Production Issues

**Last Updated:** 2026-01-13  
**Audience:** Operations, Support Engineers, DevOps  
**Environment:** SRXWEBAPP1 (IIS, .NET 8, Production)

---

## Table of Contents

1. [Connection Pool Exhaustion](#1-connection-pool-exhaustion)
2. [IIS App Pool Recycling](#2-iis-app-pool-recycling)
3. [Environment Variables Not Loaded](#3-environment-variables-not-loaded)
4. [M3 Network Connectivity](#4-m3-network-connectivity--firewall)
5. [Log File Disk Space](#5-log-file-disk-space)
6. [M3 User Permissions](#6-m3-user-account-permissions)
7. [TLS/SSL Certificate Issues](#7-tlsssl-certificate--https)
8. [API Key Security](#8-api-key-rotation--security)
9. [Circuit Breaker Stuck](#9-circuit-breaker-stays-open)
10. [Logging Performance](#10-logging-performance-impact)

---

## 1. Connection Pool Exhaustion

### Symptoms
- API returns **503 Service Unavailable**
- Error message: `Cannot acquire connection from pool. Timeout after 30000ms`
- Logs show: `[ERROR] All available connections are in use`
- Only ~10 requests succeed, then all subsequent requests fail
- M3 server shows orphaned sessions (e.g., 50+ idle connections)

### Root Cause Analysis

**Check 1: Is the pool actually exhausted?**
```powershell
# Call the pool stats endpoint
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" } `
  -ErrorAction SilentlyContinue

$response | ConvertTo-Json

# Output should show:
# {
#   "totalConnections": 10,
#   "availableConnections": 0,      # ← If 0, pool is exhausted
#   "inUseConnections": 10
# }
```

**Check 2: Are connections being properly disposed?**
```powershell
# Check logs for exception patterns
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" -Tail 100 | 
  Select-String -Pattern "exception|error|dispose" -Context 2,2

# Look for:
# - NullReferenceException in finally block
# - Socket closed unexpectedly
# - Connection not returned to pool
```

**Check 3: Is M3 server holding orphaned connections?**
```
Contact M3 Admin:
- Check active sessions on M3 (typically via System Admin menu)
- Kill orphaned sessions if > MinPoolSize + 5
- Verify M3 is not rejecting new connections
```

### Solution Steps

**Immediate (Get API back up):**
```powershell
# 1. Restart IIS app pool
Restart-WebAppPool -Name "Movex.API"

# 2. Monitor recovery
Start-Sleep -Seconds 5
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" }
$response | ConvertTo-Json

# Available should now be > 0
```

**Root Cause (Identify & Fix):**
```powershell
# A. Check for exceptions in disposal paths
# Open C:\inetpub\wwwroot\movex-api\logs\movex-api-YYYY-MM-DD.txt
# Search for patterns:
#   - "PooledConnection.Dispose()" followed by exception
#   - "finally block" errors
#   - Socket/connection timeouts

# B. If found, check TransactionService.cs line ~120:
#    finally {
#      pooled?.Dispose();  // Ensure this always runs
#    }

# C. Add connection timeout to prevent hanging:
#    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
#    await connection.SendReceiveAsync(..., cts.Token);
```

**Long-term (Prevent recurrence):**
```
1. Increase pool size if legitimate load increased:
   - Edit appsettings.Production.json:
     "ConnectionPool": { "MaxPoolSize": 20 }  // From 10
   
2. Implement pool stats monitoring:
   - Add scheduled task to log pool-stats every 5 minutes
   - Alert if availableConnections < 2
   
3. Add connection timeout:
   - Ensure no request hangs indefinitely
   - Circuit breaker will open on timeouts, allowing recovery

4. Load test before high-traffic periods:
   - Run 100 concurrent requests
   - Verify pool recovers after test completes
```

### Verify Fix
```powershell
# Run under load
$tasks = 1..50 | ForEach-Object {
  [System.Threading.Tasks.Task]::Run({
    Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/workflows/StockManagement" `
      -Headers @{ "X-API-Key" = "your-api-key" } `
      -Body '{"ITNO":"DC210055","WHLO":"D01","WHSL":"D07AA5","STQI":"50"}' `
      -ContentType "application/json" `
      -Method Post
  })
}

[System.Threading.Tasks.Task]::WaitAll($tasks)

# Check stats after
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" } | ConvertTo-Json
```

---

## 2. IIS App Pool Recycling

### Symptoms
- **Requests fail briefly** every 29 hours (or at specific time)
- Error: `Cannot execute transaction. Server error occurred.`
- Logs show: `Application is shutting down` followed by new requests
- MES shows intermittent timeouts during peak hours

### Root Cause Analysis

**Check 1: When does app pool recycle?**
```powershell
# View app pool configuration
Get-IISAppPool -Name "Movex.API" | 
  Select-Object -ExpandProperty Recycling

# Output:
# DisallowOverlappingRotation : True
# LogEventOnRecycle           : AppRecycle
# PeriodicRestart             :
# PeriodicRestartMemory       : 0
# PeriodicRestartPrivateMemory: 0
# PeriodicRestartRequests     : 0
# PeriodicRestartTime         : (empty = no scheduled recycle)

# If PeriodicRestartTime is empty, IIS recycles every 29 hours
```

**Check 2: Is memory leaking?**
```powershell
# Check app pool memory usage in Event Viewer
Get-WinEvent -LogName "System" -FilterHashtable @{
  ProviderName = "WinLogon"
  ID = 1074
} | Select-Object -First 5 TimeCreated, Message

# Look for app pool restarts caused by high memory
```

**Check 3: Did graceful shutdown fail?**
```powershell
# View last shutdown in IIS logs
Get-Content "C:\inetpub\logs\LogFiles\W3SVC1\u_ex*.log" -Tail 50 | 
  Select-String -Pattern "shutdown|recycle"
```

### Solution Steps

**Immediate (Monitor next recycle):**
```powershell
# Set app pool to recycle at controlled time (2 AM)
$pool = Get-IISAppPool -Name "Movex.API"
$pool.Recycling.PeriodicRestart.Time = [TimeSpan]"02:00:00"
$pool | Set-IISAppPool

# Extend graceful shutdown window
$pool.ProcessModel.ShutdownTimeLimit = [TimeSpan]"00:01:30"  # 90 seconds
$pool | Set-IISAppPool

# Verify
Get-IISAppPool -Name "Movex.API" | Select-Object -ExpandProperty Recycling
```

**Root Cause (Prevent forced recycling):**
```powershell
# A. Find memory leak source
#    - Run Postman collection with 1000 requests
#    - Monitor memory (Task Manager > Movex.API AppPool)
#    - If memory grows unbounded, memory leak exists
#
#    Common causes:
#    - Unclosed socket connections (check MvxSockWrapper.Dispose())
#    - Large response objects not garbage collected
#    - Event listeners not unsubscribed
#
# B. Check Serilog file handle leaks
#    - Verify logs\ directory closes file handles gracefully
#    - Test: Restart IIS, check if previous day's log file is locked
#    - If locked, add using statement to Serilog config

# C. Increase memory threshold (if leak is acceptable)
$pool = Get-IISAppPool -Name "Movex.API"
$pool.Recycling.PeriodicRestart.PrivateMemory = 512000  # 512 MB
$pool | Set-IISAppPool
```

**Long-term (Stable operation):**
```
1. Set controlled recycle time:
   appsettings.Production.json:
   {
     "IIS": {
       "RecycleTime": "02:00:00",
       "ShutdownTimeoutSeconds": 90
     }
   }

2. Monitor memory daily:
   - Add PowerShell scheduled task
   - Log app pool memory every hour
   - Alert if > 512 MB

3. Test graceful shutdown:
   - Stop app pool while requests are pending
   - Verify in-flight requests complete (not aborted)
   - Check logs for "Gracefully shutting down"
```

### Verify Fix
```powershell
# Force a controlled recycle and monitor
Stop-WebAppPool -Name "Movex.API"
Start-Sleep -Seconds 5
Start-WebAppPool -Name "Movex.API"

# Test API immediately after
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health"

# Expected: 200 OK (no errors)
```

---

## 3. Environment Variables Not Loaded

### Symptoms
- API starts but returns **500 Internal Server Error**
- Error: `Object reference not set to an instance of an object` (M3_HOST is null)
- Health endpoint returns 503
- Logs show: `M3:Host cannot be null or empty`
- Works in dev (Visual Studio), fails in IIS

### Root Cause Analysis

**Check 1: Are environment variables set?**
```powershell
# Method 1: Check IIS App Pool environment variables
Get-IISAppPool -Name "Movex.API" | 
  Get-IISAppPoolConfig | 
  Select-Object -ExpandProperty EnvironmentVariables

# Expected output:
# Name              Value
# ----              -----
# M3_HOST           150.3.2.100
# M3_PORT           6000
# M3_USER           MBUSER
# M3_PASSWORD       (encrypted)
# ApiKeys__Primary  xxx
```

**Check 2: Is the app reading from correct config source?**
```powershell
# Check Program.cs order of configuration sources
# Edit Program.cs and verify this order:
# 1. appsettings.json (defaults)
# 2. appsettings.{Environment}.json (Production overrides)
# 3. Environment Variables (app pool vars)
# 4. User Secrets (dev only)
# 5. Command line args (if any)

# View actual loaded config:
$configFile = "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"
Get-Content $configFile | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Check 3: Is the app pool identity correct?**
```powershell
# App pool must run as identity that has access to env vars
Get-IISAppPool -Name "Movex.API" | Select-Object ProcessModel

# Should show:
# ProcessModel                 : @{identityType=ApplicationPoolIdentity; ...}

# If using custom identity, verify env vars are scoped to that user:
$env:M3_HOST  # Won't show if set only for system-wide or another user
```

### Solution Steps

**Immediate (Restore connectivity):**
```powershell
# Option A: Set variables via IIS Manager UI (Recommended)
# 1. Open IIS Manager
# 2. App Pools > Movex.API > Advanced Settings
# 3. Environment Variables section
# 4. Add each variable:
#    Name: M3_HOST, Value: 150.3.2.100
#    Name: M3_PORT, Value: 6000
#    Name: M3_USER, Value: MBUSER
#    Name: M3_PASSWORD, Value: (encrypted_password)
#    Name: ApiKeys__Primary, Value: your-api-key
# 5. Restart app pool

# Option B: Set via PowerShell
$appPool = Get-IISAppPool -Name "Movex.API"
$config = Get-IISAppPoolConfig -Name "Movex.API"

# Add environment variable
$envVars = @{
  "M3_HOST" = "150.3.2.100"
  "M3_PORT" = "6000"
  "M3_USER" = "MBUSER"
}

foreach ($key in $envVars.Keys) {
  Add-IISConfigurationLocationDefault -PSPath "MACHINE/WEBROOT/APPHOST" `
    -Filter "system.applicationHost/applicationPool/add[@name='Movex.API']" `
    -ConfigAttribute @{ environmentVariables = @{ name = $key; value = $envVars[$key] } }
}

Restart-WebAppPool -Name "Movex.API"
```

**Root Cause (Verify configuration loading):**
```powershell
# A. Check appsettings.Production.json exists
Test-Path "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"

# B. Verify ASPNETCORE_ENVIRONMENT=Production
# In IIS Manager > Movex.API > Configuration Editor
# Or in app pool Environment Variables:
# ASPNETCORE_ENVIRONMENT = Production

# C. Check for typos in config keys
# Environment variables must match config structure:
# Config key "M3:Host" maps to env var "M3_HOST" (colon → underscore)
# Config key "ApiKeys:Primary" maps to env var "ApiKeys__Primary" (double underscore)
```

**Long-term (Secure & maintainable):**
```
Option 1: Use appsettings.Production.json (simple)
{
  "M3": {
    "Host": "150.3.2.100",
    "Port": "6000",
    "User": "MBUSER",
    "Password": "encrypted_in_vault"
  },
  "ApiKeys": {
    "Primary": "your-api-key"
  }
}

Option 2: Use Azure Key Vault (most secure)
// In Program.cs:
var keyvaultUrl = "https://movex-vault.vault.azure.net/";
config.AddAzureKeyVault(
  new Uri(keyvaultUrl),
  new DefaultAzureCredential()
);

Option 3: Use Windows Credential Manager
// In Program.cs:
var password = CredentialManager.ReadCredential("M3_PASSWORD");

Recommendation: Use Option 1 for dev/test, Option 2 for production
```

### Verify Fix
```powershell
# Restart app pool and check health
Restart-WebAppPool -Name "Movex.API"
Start-Sleep -Seconds 3

Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health"

# Expected: 200 OK
# Response:
# {
#   "status": "Healthy",
#   "timestamp": "2026-01-13T14:30:00Z",
#   "checks": {
#     "M3Connectivity": "Healthy",
#     "ConfigurationReady": "Healthy"
#   }
# }
```

---

## 4. M3 Network Connectivity / Firewall

### ⚠️ Dev Machine Prerequisite — GlobalProtect VPN

**All Scanfil APAC infrastructure (`150.3.2.x`, IBM i AS400, M3 MI server) is on the office LAN.**
When developing from outside the office you must connect **GlobalProtect VPN** first.

**Symptoms when VPN is off:**

| Error | Meaning |
|---|---|
| `comm rc=11001 - CWBCO1004 - Remote address could not be resolved` | DB2/ODBC can't reach IBM i — VPN off |
| `Connection refused` on port 6300 | M3 MI socket unreachable — VPN off |
| `ping 150.3.2.100` → 100% packet loss | No route to office LAN — VPN off |

**Quick check:**
```powershell
# If this fails, connect GlobalProtect before doing anything else
Test-NetConnection -ComputerName 150.3.2.100 -Port 6300 -InformationLevel Quiet
```

**Fix:** Connect GlobalProtect, then restart the API so it picks up a fresh connection pool.

---

### Symptoms (Production / SRXWEBAPP1)
- API returns **502 Bad Gateway** or **503 Service Unavailable**
- All requests immediately fail (circuit breaker opens)
- Error: `Connection refused` or `No route to host`
- `Test-NetConnection` fails
- M3 server offline or unreachable

### Root Cause Analysis

**Check 1: Is M3 server online?**
```powershell
# Test from SRXWEBAPP1
Test-NetConnection -ComputerName 150.3.2.100 -Port 6000 -InformationLevel Detailed

# Expected output:
# ComputerName   : 150.3.2.100
# RemoteAddress  : 150.3.2.100
# RemotePort     : 6000
# TcpTestSucceeded : True
```

**Check 2: Can SRXWEBAPP1 reach M3?**
```powershell
# Check routing
$route = (Get-NetRoute -DestinationPrefix "150.3.2.0/24" -ErrorAction SilentlyContinue)
if ($route) {
  Write-Host "Route found: $($route.NextHop)"
} else {
  Write-Host "No route to 150.3.2.0/24 - network unreachable"
}

# Check firewall
$fw = Get-NetFirewallRule -Direction Outbound -Action Allow -ErrorAction SilentlyContinue | 
  Where-Object { $_.RemotePort -eq 6000 }
if (-not $fw) {
  Write-Host "No outbound firewall rule for port 6000 - blocked"
}
```

**Check 3: Is DNS working?**
```powershell
# If M3 is referenced by hostname
Resolve-DnsName m3-server.company.com

# Should return IP address, not error
```

### Solution Steps

**Immediate (Diagnose connectivity):**
```powershell
# Run full diagnostic
Write-Host "=== M3 Connectivity Diagnostic ===" -ForegroundColor Green

# 1. Ping (ICMP)
Test-NetConnection -ComputerName 150.3.2.100 -ErrorAction SilentlyContinue
Write-Host "`n"

# 2. Port connectivity
Test-NetConnection -ComputerName 150.3.2.100 -Port 6000 -InformationLevel Detailed
Write-Host "`n"

# 3. Check M3 service on server
# Contact M3 admin: "Is M3 MI listener running on port 6000?"
Write-Host "Contact M3 Admin - Verify M3 MI listener is running`n" -ForegroundColor Yellow

# 4. Check logs
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" -Tail 50 | 
  Select-String -Pattern "connection|refused|timeout"
```

**Root Cause (Restore connectivity):**
```powershell
# If Test-NetConnection fails:
# Contact Network Team with:
# - Source: SRXWEBAPP1 (10.1.50.x)
# - Destination: 150.3.2.100:6000
# - Requirement: Enable outbound TCP on port 6000

# Firewall rule to add (if you have permission):
New-NetFirewallRule -DisplayName "Allow M3 Port 6000" `
  -Direction Outbound `
  -Action Allow `
  -Protocol TCP `
  -RemoteAddress 150.3.2.100 `
  -RemotePort 6000

# If M3 server is offline:
# 1. Contact M3 team to start MI listener
# 2. Verify M3 application is running
# 3. Check M3 logs for errors
```

**Restart M3 connection pool after network recovery:**
```powershell
# When connectivity is restored
Restart-WebAppPool -Name "Movex.API"

# Monitor circuit breaker recovery
Start-Sleep -Seconds 5

# Make a test request
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" }

# Should show availableConnections > 0
```

### Verify Fix
```powershell
# Test M3 connectivity directly
$host = "150.3.2.100"
$port = 6000

$tcpClient = New-Object System.Net.Sockets.TcpClient
try {
  $tcpClient.Connect($host, $port)
  Write-Host "✓ Connected to M3 successfully" -ForegroundColor Green
  $tcpClient.Close()
} catch {
  Write-Host "✗ Connection failed: $_" -ForegroundColor Red
}

# Test via API
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health" | ConvertTo-Json
```

---

## 5. Log File Disk Space

### Symptoms
- Disk fills up quickly (within days/weeks)
- Error: `The disk is full` or `No space left on device`
- App stops logging (`logs/movex-api-*.txt` stops being written)
- IIS app pool may crash
- C: drive shows **100% full**

### Root Cause Analysis

**Check 1: How much space do logs consume?**
```powershell
$logDir = "C:\inetpub\wwwroot\movex-api\logs"
$logSize = (Get-ChildItem $logDir -Recurse | 
  Measure-Object -Property Length -Sum).Sum

$logSizeMB = [math]::Round($logSize / 1MB, 2)
Write-Host "Total log size: $logSizeMB MB"

# Check daily growth
$todayLogs = Get-ChildItem $logDir -Filter "movex-api-$(Get-Date -Format yyyy-MM-dd)*"
$todaySize = ($todayLogs | Measure-Object -Property Length -Sum).Sum
$todaySizeMB = [math]::Round($todaySize / 1MB, 2)
Write-Host "Today's logs: $todaySizeMB MB"

# Extrapolate
$dailyGrowthMB = $todaySizeMB
$daysUntilFull = 50 * 1024 / $dailyGrowthMB  # Assume 50 GB free
Write-Host "Days until disk full: $daysUntilFull"
```

**Check 2: What's being logged at high volume?**
```powershell
# Analyze log patterns
$logFile = Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs" -Filter "movex-api-*" | 
  Sort-Object LastWriteTime -Descending | Select-Object -First 1

# Count log levels
Get-Content $logFile.FullName | 
  Select-String -Pattern "\[ERROR\]|\[WARN\]|\[INFO\]|\[DEBUG\]" |
  ForEach-Object { $_ -replace '.*\[([A-Z]+)\].*', '$1' } |
  Group-Object | Sort-Object Count -Descending

# If DEBUG or VERBOSE count is high, that's the issue
```

**Check 3: Is log retention configured?**
```powershell
# Check appsettings.json
$configPath = "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"
$config = Get-Content $configPath | ConvertFrom-Json

$config.Serilog.WriteTo[0]  # Should show retainedFileCountLimit

# Expected:
# {
#   "Name": "File",
#   "Args": {
#     "path": "logs/movex-api-.txt",
#     "rollingInterval": "Day",
#     "retainedFileCountLimit": 30  # ← Keep only 30 days
#   }
# }
```

### Solution Steps

**Immediate (Free up space):**
```powershell
# 1. Compress old logs
$logDir = "C:\inetpub\wwwroot\movex-api\logs"
$cutoffDate = (Get-Date).AddDays(-7)  # Keep last 7 days uncompressed

Get-ChildItem $logDir -Filter "movex-api-*.txt" | 
  Where-Object { $_.LastWriteTime -lt $cutoffDate } |
  ForEach-Object {
    Compress-Archive -Path $_.FullName `
      -DestinationPath "$($_.FullName).zip" `
      -Force
    Remove-Item $_.FullName
  }

Write-Host "Compressed logs older than 7 days"

# 2. Check freed space
$drive = Get-Volume -DriveLetter C
$freeGB = [math]::Round($drive.SizeRemaining / 1GB, 2)
Write-Host "Free space: $freeGB GB"
```

**Root Cause (Reduce logging volume):**
```powershell
# A. Reduce log level in production
#    Edit appsettings.Production.json:
#    "Serilog": {
#      "MinimumLevel": "Warning"  # Change from Debug
#    }

# B. Or, implement log filtering
#    Only log warnings/errors, not debug
$configPath = "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"
$config = Get-Content $configPath | ConvertFrom-Json

$config.Serilog.MinimumLevel = "Warning"
$config | ConvertTo-Json | Set-Content $configPath

# C. Lower retention if still too much
$config.Serilog.WriteTo[0].Args.retainedFileCountLimit = 15  # From 30

# D. Restart app pool for changes to take effect
Restart-WebAppPool -Name "Movex.API"
```

**Long-term (Monitoring & cleanup):**
```powershell
# Create scheduled task for log maintenance
$taskName = "Movex-Log-Cleanup"

# Define task action
$action = New-ScheduledTaskAction -Execute "powershell.exe" `
  -Argument @"
`$logDir = 'C:\inetpub\wwwroot\movex-api\logs'
`$cutoffDate = (Get-Date).AddDays(-30)
Get-ChildItem `$logDir -Filter "*.zip" | 
  Where-Object { `$_.LastWriteTime -lt `$cutoffDate } | 
  Remove-Item
"@

# Define schedule (daily at 3 AM)
$trigger = New-ScheduledTaskTrigger -Daily -At "03:00:00"

# Register task
Register-ScheduledTask -TaskName $taskName `
  -Action $action `
  -Trigger $trigger `
  -RunLevel Highest `
  -Force

Write-Host "Scheduled task created: $taskName"

# Add monitoring alert
# If logs > 30 GB, send email alert (implement via monitoring tool)
```

### Verify Fix
```powershell
# Monitor disk space after cleanup
$drive = Get-Volume -DriveLetter C
$percentFree = [math]::Round(($drive.SizeRemaining / $drive.Size) * 100, 1)
Write-Host "Disk: $percentFree% free"

# Expected: > 20% free space
# Alert if < 10% free space
```

---

## 6. M3 User Account Permissions

### Symptoms
- API validation passes, but M3 transaction fails
- Error: `Access denied` or `Program not found in menu`
- Logs show: `MMS200MI is not available for user MBUSER`
- Stock adjustment returns 403 or 401 errors
- GetStandardCost returns empty cost

### Root Cause Analysis

**Check 1: Does M3 user have company access?**
```
Contact M3 Admin:
1. Go to M3 System Admin > User Information > MBUSER
2. Check "Company" field
3. Should include: 300 (or whatever your company code is)
4. Verify user is not locked/disabled
```

**Check 2: Does user have program menu access?**
```
In M3 System Admin:
1. User Information > MBUSER > Programs
2. Should include:
   - MMS200MI (Item Master - Get Basic)
   - MMS310MI (Stock Balance - Post Transaction)
   - MMS003 (Stock Balance Inquiry)
3. Check any missing programs
```

**Check 3: Does user have facility/warehouse access?**
```
In M3 Warehouse Control:
1. Check if MBUSER has access to warehouse D01
2. Check if MBUSER can adjust stock in WHSL (stock location)
3. Verify user is not restricted to specific facilities
```

### Solution Steps

**Immediate (Verify access):**
```powershell
# Test with Postman to identify exact failure point
$headers = @{ "X-API-Key" = "your-api-key" }

# 1. Test validation (this should pass)
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/workflows/StockManagement" `
  -Method Post `
  -Headers $headers `
  -Body '{"ITNO":"DC210055","WHLO":"D01","WHSL":"D07AA5","STQI":"50"}' `
  -ContentType "application/json" `
  -ErrorVariable err

if ($err) {
  Write-Host "Validation failed: $err" -ForegroundColor Red
  # Permission issue at validation stage
} else {
  Write-Host "Validation passed: $($response | ConvertTo-Json)" -ForegroundColor Green
  # Permission issue at M3 transaction stage
}

# 2. Check logs for specific M3 error
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" -Tail 100 |
  Select-String -Pattern "MMS|access|permission|menu"
```

**Root Cause (Request permissions):**
```
Submit to M3 Admin (provide this info):

Access Request for User: MBUSER
Company: 300
Programs Required:
  - MMS200MI (access level: Execute)
  - MMS310MI (access level: Execute)
  - MMS003 (access level: Execute)
Facilities/Warehouses: D01, D02 (or all if not facility-restricted)
Special Requirements:
  - Stock adjustment transactions
  - Query-only access to item cost information

Expected Timeline: 1-2 business days
```

**Verify access (manual test in M3):**
```
1. Log into M3 as MBUSER
2. Navigate to MMS200MI manually
3. If program doesn't appear: permission issue
4. If program appears but transaction fails: data access issue
5. Test retrieving item DC210055 from company 300
6. Test querying standard cost
```

### Verify Fix
```powershell
# Retry workflow after permissions granted
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/workflows/StockManagement" `
  -Method Post `
  -Headers @{ "X-API-Key" = "your-api-key" } `
  -Body '{"ITNO":"DC210055","WHLO":"D01","WHSL":"D07AA5","STQI":"50"}' `
  -ContentType "application/json"

$response | ConvertTo-Json

# Expected: Success response with transaction results
# If still failing, escalate to M3 admin with full error message
```

---

## 7. TLS/SSL Certificate & HTTPS

### Symptoms
- Browser shows `SSL_ERROR_CERT_INVALID` or `CERT_UNTRUSTED`
- Postman shows certificate validation warning
- MES cannot connect to API over HTTPS
- API accessible via HTTP but not HTTPS
- Certificate binding missing in IIS

### Root Cause Analysis

**Check 1: Is certificate installed?**
```powershell
# List installed certificates
Get-ChildItem Cert:\LocalMachine\My | 
  Where-Object { $_.Subject -match "SRXWEBAPP1" }

# Expected: Certificate for SRXWEBAPP1 or *.company.com
# If empty: certificate not installed
```

**Check 2: Is HTTPS binding configured in IIS?**
```powershell
# View site bindings
Get-WebBinding -Name "Default Web Site"

# Expected output should include:
# Protocol: https
# Port: 443
# HostHeader: SRXWEBAPP1 (or domain)
# CertificateHash: (thumbprint of cert)

# If no HTTPS binding: binding not configured
```

**Check 3: Is certificate valid?**
```powershell
$cert = Get-ChildItem Cert:\LocalMachine\My | 
  Where-Object { $_.Subject -match "SRXWEBAPP1" } | 
  Select-Object -First 1

if ($cert) {
  Write-Host "Certificate Subject: $($cert.Subject)"
  Write-Host "Expires: $($cert.NotAfter)"
  Write-Host "Thumbprint: $($cert.Thumbprint)"
  
  $daysUntilExpire = ($cert.NotAfter - (Get-Date)).Days
  if ($daysUntilExpire -lt 30) {
    Write-Host "⚠ Certificate expires in $daysUntilExpire days!" -ForegroundColor Yellow
  }
}
```

### Solution Steps

**Immediate (Enable HTTPS):**
```powershell
# 1. Obtain certificate
# Option A: Self-signed (dev/testing only)
$cert = New-SelfSignedCertificate -DnsName "SRXWEBAPP1" `
  -CertStoreLocation "Cert:\LocalMachine\My" `
  -NotAfter (Get-Date).AddYears(1)

# Option B: From Certificate Authority (production)
# Request from IT: subject=SRXWEBAPP1.company.com, wildcard=*.company.com

# 2. Add HTTPS binding to IIS
New-WebBinding -Name "Default Web Site" `
  -IP "*" `
  -Port 443 `
  -Protocol https `
  -HostHeader "SRXWEBAPP1" `
  -CertificateThumbprint $cert.Thumbprint `
  -SslFlags "Sni,CentralCertStore"  # SNI for hostname-based binding

# 3. Test HTTPS
$null = @"
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

ServicePointManager.ServerCertificateValidationCallback = 
  new RemoteCertificateValidationCallback((a,b,c,d) => true);
"@

Add-Type $null

Invoke-RestMethod -Uri "https://SRXWEBAPP1:443/health" `
  -SkipCertificateCheck  # Skip validation for self-signed
```

**Root Cause (Renew if expired):**
```powershell
# If certificate is expired
Get-ChildItem Cert:\LocalMachine\My | 
  Where-Object { $_.NotAfter -lt (Get-Date) } | 
  ForEach-Object {
    Write-Host "Expired certificate: $($_.Subject)" -ForegroundColor Red
    Write-Host "Thumbprint: $($_.Thumbprint)"
    # Remove old binding, obtain new cert, create new binding
  }
```

**Long-term (Maintenance):**
```powershell
# Create reminder for certificate renewal (60 days before expiry)
$cert = Get-ChildItem Cert:\LocalMachine\My | 
  Where-Object { $_.Subject -match "SRXWEBAPP1" }

$renewalDate = $cert.NotAfter.AddDays(-60)
$daysUntilReminder = ($renewalDate - (Get-Date)).Days

Write-Host "Certificate renewal reminder: $daysUntilReminder days from now"

# Automate renewal (if using Azure Key Vault or Let's Encrypt)
# Schedule annual task to request new certificate
```

### Verify Fix
```powershell
# Test HTTPS connectivity
$uri = "https://SRXWEBAPP1/api/health"
try {
  $response = Invoke-WebRequest -Uri $uri -SkipCertificateCheck
  Write-Host "✓ HTTPS connection successful (Status: $($response.StatusCode))"
} catch {
  Write-Host "✗ HTTPS connection failed: $_" -ForegroundColor Red
}

# Check certificate in browser
# Navigate to https://SRXWEBAPP1
# Click lock icon > View certificate
# Verify subject matches SRXWEBAPP1 and not expired
```

---

## 8. API Key Rotation & Security

### Symptoms
- API key exposed in logs or configuration
- Unauthorized requests using leaked key
- Cannot rotate keys without downtime
- Audit log shows suspicious API key usage

### Root Cause Analysis

**Check 1: Is API key in logs?**
```powershell
# Search for API key patterns in logs
Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs\*" -Filter "*.txt" |
  ForEach-Object {
    $content = Get-Content $_.FullName
    if ($content -match "X-API-Key|ApiKeys|api.?key") {
      Write-Host "⚠ Potential API key exposure in: $($_.Name)" -ForegroundColor Yellow
    }
  }

# Check Postman collection
$postmanPath = "C:\inetpub\wwwroot\movex-api\postman-collections\*"
if (Get-ChildItem $postmanPath | Select-String -Pattern "api.?key" -List) {
  Write-Host "⚠ API key may be in Postman collection!" -ForegroundColor Yellow
}
```

**Check 2: Is key stored securely?**
```powershell
# Check appsettings files
$appSettingsPath = "C:\inetpub\wwwroot\movex-api\appsettings.json"
if (Get-Content $appSettingsPath | Select-String -Pattern "ApiKeys") {
  Write-Host "⚠ API key in appsettings.json (should be in env vars)" -ForegroundColor Yellow
}
```

### Solution Steps

**Immediate (Rotate compromised key):**
```powershell
# Method 1: Via API endpoint (requires current admin key)
$newKey = "new-api-key-" + (New-Guid).ToString().Substring(0, 20)

$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/security/api-key/rotate" `
  -Method Post `
  -Headers @{ "X-Admin-Key" = "current-admin-key" } `
  -Body @{ newPrimaryKey = $newKey } -ContentType "application/json"

Write-Host "New API Key: $($response.newKey)"

# Method 2: Update environment variable (requires restart)
$appPool = Get-IISAppPool -Name "Movex.API"
# Update env var ApiKeys__Primary with new key
# Restart-WebAppPool -Name "Movex.API"
```

**Root Cause (Prevent exposure):**
```powershell
# 1. Remove key from logs (if found)
#    Edit Serilog config in appsettings.json:
#    "Serilog": {
#      "Destructure": [
#        {
#          "Name": "WithProperty",
#          "Args": {
#            "name": "X-API-Key",
#            "value": "***REDACTED***"
#          }
#        }
#      ]
#    }

# 2. Remove key from Postman collection
#    - Open postman-collections/*.json
#    - Remove any X-API-Key values
#    - Replace with {{api-key}} variable
#    - Store actual key in Postman environment only

# 3. Audit who has access to keys
Get-Content "C:\inetpub\wwwroot\movex-api\appsettings.Production.json" |
  Select-String -Pattern "ApiKeys"
# Should NOT show actual key value

# 4. Add rate limiting to prevent brute force
#    Implement in Program.cs:
#    services.AddRateLimiting(options => {
#      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
#        RateLimitPartition.GetFixedWindowLimiter(
#          partitionKey: context.Request.Headers["X-API-Key"],
#          factory: _ => new FixedWindowRateLimiterOptions {
#            AutoReplenishment = true,
#            PermitLimit = 100,
#            Window = TimeSpan.FromMinutes(1)
#          }));
#    });
```

**Long-term (Key management):**
```powershell
# Implement key rotation policy
# - Primary key: rotate every 30 days
# - Secondary key: maintain for gradual rollover
# - Audit key usage: track requests per key
# - Alert on unusual patterns: same key from multiple IPs

# Example rotation schedule:
# Day 1: Generate new key, set as Secondary
# Day 8: Publish Secondary key to users
# Day 15: Monitor - most traffic on Secondary
# Day 22: Set Secondary as Primary
# Day 29: Disable old Primary (30-day window)

# Use API endpoint:
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/security/api-keys" `
  -Headers @{ "X-Admin-Key" = "admin-key" } | ConvertTo-Json

# Should show:
# {
#   "keys": [
#     { "name": "primary", "createdAt": "2026-01-13", "lastUsed": "2026-01-13T14:30:00Z" },
#     { "name": "secondary", "createdAt": "2026-01-06", "lastUsed": "2026-01-13T12:00:00Z" }
#   ]
# }
```

### Verify Fix
```powershell
# 1. Confirm key rotation
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/health" `
  -Headers @{ "X-API-Key" = "new-api-key-xxx" }

# 2. Old key should fail
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/health" `
  -Headers @{ "X-API-Key" = "old-api-key-xxx" } `
  -ErrorVariable err

if ($err) {
  Write-Host "✓ Old key properly disabled"
}

# 3. Audit log shows key rotation
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" |
  Select-String -Pattern "ApiKey|rotate|security"
```

---

## 9. Circuit Breaker Stays Open

### Symptoms
- **All requests return 503 Service Unavailable**
- Even after M3 is restored, API still fails
- Logs show: `Circuit breaker is open`
- Users see API down even though M3 is healthy
- Manual API call to circuit breaker reset endpoint doesn't exist

### Root Cause Analysis

**Check 1: Why is circuit open?**
```powershell
# Circuit breaker opens after 3 consecutive failures
# Configuration in PollyPolicies.cs:
#   - Failures before opening: 3
#   - Duration circuit stays open: 30 seconds
#   - Requires half-open test to close

# Check logs for failure pattern
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" -Tail 100 |
  Select-String -Pattern "circuit|open|half-open|failure"

# Expected pattern:
# [INFO] Executing M3 transaction attempt 1 of 3
# [ERROR] Failure 1: Connection timeout
# [INFO] Executing M3 transaction attempt 2 of 3
# [ERROR] Failure 2: Connection timeout
# [INFO] Executing M3 transaction attempt 3 of 3
# [ERROR] Failure 3: Connection timeout
# [WARN] Circuit breaker opened. Waiting 30 seconds before retry
```

**Check 2: Is M3 actually back online?**
```powershell
# Even if circuit shows open, verify M3 connectivity
Test-NetConnection -ComputerName 150.3.2.100 -Port 6000 -InformationLevel Detailed

# If connection succeeds but circuit still open:
# - Circuit needs half-open test
# - Wait for 30-second timer, then make a request
# - If request succeeds, circuit closes
```

### Solution Steps

**Immediate (Restore service):**
```powershell
# Option 1: Wait for automatic recovery (30 seconds)
Write-Host "Circuit breaker will attempt recovery in 30 seconds..."
Start-Sleep -Seconds 30

# Make a request to trigger half-open test
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" }

# If successful, circuit closes automatically
```

**Forced recovery (if needed):**
```powershell
# Option 2: Restart app pool (force circuit reset)
Restart-WebAppPool -Name "Movex.API"
Write-Host "App pool restarted. Circuit state reset."

# Verify
Start-Sleep -Seconds 3
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health"
```

**Root Cause (Prevent prolonged outages):**
```powershell
# A. Reduce circuit open duration if 30s is too long
#    Edit PollyPolicies.cs:
#    .CircuitBreakerAsync(
#      handledEventsAllowedBeforeBreaking: 3,
#      durationOfBreak: TimeSpan.FromSeconds(15)  # From 30
#    );

# B. Increase tolerance if failures are transient
#    Currently: 3 failures before open
#    Change to: 5 failures to allow for transient issues

# C. Add logging to circuit breaker state transitions
#    When circuit opens: log why + expected recovery time
```

### Verify Fix
```powershell
# Simulate circuit breaker behavior
Write-Host "=== Circuit Breaker Test ===" -ForegroundColor Green

# 1. Verify circuit is closed
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/transactions/pool-stats" `
  -Headers @{ "X-API-Key" = "your-api-key" }
Write-Host "✓ Circuit closed: availableConnections = $($response.availableConnections)"

# 2. Simulate failure (stop M3 or firewall)
# ... (have network team block 150.3.2.100:6000)

# 3. Make requests until circuit opens
for ($i = 1; $i -le 5; $i++) {
  $result = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health" `
    -ErrorVariable err -ErrorAction SilentlyContinue
  if ($err) {
    Write-Host "Request $i: Failed (expected)"
    if ($i -ge 3) {
      Write-Host "⚠ Circuit should be open now"
    }
  }
  Start-Sleep -Seconds 1
}

# 4. Restore M3 connectivity
# ... (have network team unblock 150.3.2.100:6000)

# 5. Wait 30 seconds for circuit to attempt half-open
Start-Sleep -Seconds 30

# 6. Make request to trigger close
$response = Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health"
Write-Host "✓ Circuit closed after recovery"
```

---

## 10. Logging Performance Impact

### Symptoms
- API response times slower in production vs. development
- Average response time: 200ms → 500ms+
- CPU usage spikes during high request volume
- Disk I/O bottleneck during peak hours
- Log file writes are blocking requests

### Root Cause Analysis

**Check 1: What's the logging overhead?**
```powershell
# Check current log level
$configPath = "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"
$config = Get-Content $configPath | ConvertFrom-Json
Write-Host "Minimum Log Level: $($config.Serilog.MinimumLevel)"

# If "Debug" or "Verbose": excessive logging
# Expected: "Information" or "Warning" in production
```

**Check 2: Are log writes synchronous?**
```powershell
# Check Serilog configuration
$config.Serilog.WriteTo[0].Args | ConvertTo-Json

# If "buffered": false, writes are synchronous (slow)
# If "buffered": true, writes are async (fast)
```

**Check 3: What's the disk I/O impact?**
```powershell
# Monitor disk performance during test
$before = Get-Counter "\\SRXWEBAPP1\PhysicalDisk(_Total)\Disk Bytes/sec"

# Run load test (100 concurrent requests)
# ...

$after = Get-Counter "\\SRXWEBAPP1\PhysicalDisk(_Total)\Disk Bytes/sec"
$diskThroughput = $after.CounterSamples[0].CookedValue
Write-Host "Disk throughput: $diskThroughput MB/s"

# If > 50 MB/s during logging: I/O bottleneck
```

### Solution Steps

**Immediate (Reduce logging impact):**
```powershell
# 1. Lower log level in production
$configPath = "C:\inetpub\wwwroot\movex-api\appsettings.Production.json"
$config = Get-Content $configPath | ConvertFrom-Json

$config.Serilog.MinimumLevel = "Warning"  # From Debug

# 2. Enable buffered writes
$config.Serilog.WriteTo[0].Args.buffered = $true
$config.Serilog.WriteTo[0].Args.flushInterval = 1000  # Flush every 1s

# 3. Reduce log retention (fewer files to manage)
$config.Serilog.WriteTo[0].Args.retainedFileCountLimit = 15  # From 30

$config | ConvertTo-Json | Set-Content $configPath

# 4. Restart for changes to take effect
Restart-WebAppPool -Name "Movex.API"
```

**Root Cause (Optimize logging):**
```powershell
# A. Add structured logging for performance tracking
#    In TransactionService.cs:
#    var sw = Stopwatch.StartNew();
#    await connection.SendReceiveAsync(...);
#    sw.Stop();
#    _logger.LogInformation("M3 transaction completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
#
#    This allows filtering by slow transactions in logs

# B. Use sampling to reduce volume (log 10% of requests)
#    "Serilog": {
#      "MinimumLevel": "Warning",
#      "Enrich": ["WithMachineName", "WithThreadId"],
#      "Destructure": [
#        {
#          "Name": "ToMaximumDepth",
#          "Args": { "maximumDestructuringDepth": 4 }
#        }
#      ]
#    }

# C. Use async file sink (non-blocking writes)
#    // In Program.cs:
#    .WriteTo.File(
#      path: "logs/movex-api-.txt",
#      buffered: true,
#      flushInterval: TimeSpan.FromSeconds(1),
#      rollingInterval: RollingInterval.Day
#    )
```

**Long-term (Monitor performance):**
```powershell
# Create performance baseline
$baseline = @{
  AverageResponseTime = 0
  P95ResponseTime = 0
  P99ResponseTime = 0
  LogWriteTime = 0
  DiskI_O = 0
}

# Run 1000 requests and measure
$times = @()
1..1000 | ForEach-Object {
  $start = Get-Date
  Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/health" `
    -Headers @{ "X-API-Key" = "your-api-key" } `
    -ErrorAction SilentlyContinue | Out-Null
  $elapsed = (Get-Date) - $start
  $times += $elapsed.TotalMilliseconds
}

$times | Sort-Object | Select-Object -Index 0, ([int]($times.Count * 0.95)), ([int]($times.Count * 0.99)) |
  ConvertTo-Json

# Expected:
# [
#   5,      # Min (best case)
#   150,    # P95
#   250     # P99
# ]

# If P95 > 300ms: logging is impacting performance
```

### Verify Fix
```powershell
# Re-run performance test after optimizations
$times = @()
1..500 | ForEach-Object {
  $start = Get-Date
  Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/api/health" `
    -Headers @{ "X-API-Key" = "your-api-key" } `
    -ErrorAction SilentlyContinue | Out-Null
  $elapsed = (Get-Date) - $start
  $times += $elapsed.TotalMilliseconds
}

$avg = ($times | Measure-Object -Average).Average
$p95 = $times | Sort-Object | Select-Object -Index ([int]($times.Count * 0.95))
$p99 = $times | Sort-Object | Select-Object -Index ([int]($times.Count * 0.99))

Write-Host "Average: $avg ms"
Write-Host "P95: $p95 ms"
Write-Host "P99: $p99 ms"

# Expected after optimization: < 100ms average
```

---

## Quick Reference: Emergency Recovery

```powershell
# If API is completely down:

# 1. Check app pool status
Get-IISAppPool -Name "Movex.API" | Select-Object State

# 2. Check for obvious issues
Test-NetConnection -ComputerName 150.3.2.100 -Port 6000  # M3 connectivity
Get-Volume -DriveLetter C | Select-Object SizeRemaining  # Disk space
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt" -Tail 20  # Recent errors

# 3. Restart app pool (usually fixes transient issues)
Restart-WebAppPool -Name "Movex.API"
Start-Sleep -Seconds 5

# 4. Test health endpoint
Invoke-RestMethod -Uri "http://SRXWEBAPP1:5000/health" | ConvertTo-Json

# 5. If still down, escalate to M3 admin (likely external issue)
```

---

## Related Documentation

- [DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md) - Pre-deployment validation
- [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) - IIS setup & configuration
- [RUNBOOK.md](RUNBOOK.md) - Standard operational procedures
- [LOGGING_SETUP.md](LOGGING_SETUP.md) - Serilog configuration details
