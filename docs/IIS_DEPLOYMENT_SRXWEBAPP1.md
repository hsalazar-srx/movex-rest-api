# IIS Deployment Guide - SRXWEBAPP1

**Target Server:** SRXWEBAPP1  
**Platform:** Windows Server (IIS)  
**Estimated Time:** 1-2 hours  
**Runtime Required:** .NET 8.0 Runtime  

---

## Table of Contents
1. [Pre-Deployment Checklist](#pre-deployment-checklist)
2. [Step 1: Prepare SRXWEBAPP1](#step-1-prepare-srxwebapp1)
3. [Step 2: Publish Application](#step-2-publish-application)
4. [Step 3: IIS Configuration](#step-3-iis-configuration)
5. [Step 4: Environment Variables & Secrets](#step-4-environment-variables--secrets)
6. [Step 5: Logging Setup](#step-5-logging-setup)
7. [Step 6: Verification & Health Checks](#step-6-verification--health-checks)
8. [Step 7: Production Hardening](#step-7-production-hardening)
9. [Troubleshooting](#troubleshooting)
10. [Rollback Procedure](#rollback-procedure)

---

## Pre-Deployment Checklist

Before starting, verify:

- [ ] **SRXWEBAPP1 Server Access**: Can RDP/Remote into server
- [ ] **.NET 8.0 Runtime**: Installed on SRXWEBAPP1 (`dotnet --version` returns 8.0.x)
- [ ] **IIS Enabled**: IIS role installed with these features:
  - [ ] Web Server (IIS)
  - [ ] Application Development > .NET Core Hosting Bundle
  - [ ] Application Development > ASP.NET Core Runtime
  - [ ] Management Tools > IIS Management Console
- [ ] **Network Access**: SRXWEBAPP1 can reach M3 server on port 6000 (test with `Test-NetConnection -ComputerName 150.3.2.100 -Port 6000`)
- [ ] **Disk Space**: At least 500 MB free space for app files and logs
- [ ] **M3 Credentials**: Have M3 username, password, and company code (300) verified
- [ ] **API Key**: Generated and ready (`.\scripts\generate-api-key-simple.ps1` from dev machine)

---

## Step 1: Prepare SRXWEBAPP1

### 1.1 Verify .NET 8.0 Runtime

SSH/RDP into SRXWEBAPP1:

```powershell
# Check installed .NET versions
dotnet --list-runtimes

# Output should include:
# Microsoft.AspNetCore.App 8.0.x [C:\Program Files\dotnet\...]
# Microsoft.NETCore.App 8.0.x [C:\Program Files\dotnet\...]
```

If not installed, download from: https://dotnet.microsoft.com/download/dotnet/8.0

**Important:** Use the **Runtime** version (not SDK), unless you plan to build on the server.

### 1.2 Verify IIS Configuration

```powershell
# Verify IIS is installed
Get-WindowsFeature Web-Server

# Verify .NET Core Hosting Bundle
Get-WindowsFeature Web-AppInit, Web-Asp-Net45

# Restart IIS to ensure modules are loaded
iisreset /restart
```

### 1.3 Create Application Directory

```powershell
# Create directory for the API
mkdir C:\inetpub\wwwroot\movex-api

# Create logs subdirectory
mkdir C:\inetpub\wwwroot\movex-api\logs

# Set permissions (IIS AppPool user needs write access to logs)
# For IIS AppPool identity (default):
$aclRules = Get-Acl "C:\inetpub\wwwroot\movex-api\logs"
$aclRules.SetAccessRuleProtection($false, $true)
Set-Acl "C:\inetpub\wwwroot\movex-api\logs" $aclRules

# Or simplest approach: give AppPool user (IUSR) full control
icacls "C:\inetpub\wwwroot\movex-api" /grant "IUSR:(OI)(CI)F" /T
icacls "C:\inetpub\wwwroot\movex-api\logs" /grant "IIS AppPool\DefaultAppPool:(OI)(CI)F" /T
```

---

## Step 2: Publish Application

### 2.1 Publish from Development Machine

On your **local development machine** (where code is checked out):

```powershell
cd C:\Projects\MOVEX\API-Integration\movex-rest-api

# Clean previous builds
dotnet clean -c Release

# Publish in Release configuration
dotnet publish -c Release -o "C:\temp\movex-api-release"

# Output: ~45 MB of files in C:\temp\movex-api-release
```

### 2.2 Verify Published Files

```powershell
dir "C:\temp\movex-api-release"

# Should contain:
# - Movex.API.exe
# - appsettings.json
# - appsettings.Production.json (optional)
# - wwwroot/ (if any static files)
# - transactions/ (config directory)
# - *.dll files (dependencies)
```

### 2.3 Transfer to SRXWEBAPP1

Use one of these methods:

**Method A: RDP Copy-Paste**
```powershell
# On dev machine: Zip the files
Compress-Archive -Path "C:\temp\movex-api-release" -DestinationPath "C:\temp\movex-api.zip"

# Copy movex-api.zip to SRXWEBAPP1 via shared drive or RDP
# On SRXWEBAPP1: Extract
Expand-Archive -Path "C:\temp\movex-api.zip" -DestinationPath "C:\inetpub\wwwroot\movex-api"
```

**Method B: Network Share**
```powershell
# Share the folder on dev machine and copy via UNC path
copy-item "C:\temp\movex-api-release\*" "\\SRXWEBAPP1\c$\inetpub\wwwroot\movex-api" -Recurse
```

**Method C: Git Clone (if repo access available)**
```powershell
# On SRXWEBAPP1
cd C:\inetpub\wwwroot\movex-api
git clone https://github.com/your-org/API-Integration.git
cd movex-rest-api
dotnet publish -c Release -o "C:\inetpub\wwwroot\movex-api"
```

### 2.4 Verify File Transfer

```powershell
dir C:\inetpub\wwwroot\movex-api

# Confirm files are present:
# - Movex.API.exe
# - appsettings.json
# - transactions/ folder
```

---

## Step 3: IIS Configuration

### 3.1 Create Application Pool

On SRXWEBAPP1, open **IIS Manager** or use PowerShell:

**Option A: IIS Manager GUI**
1. Open **IIS Manager** → **Application Pools**
2. Right-click → **Add Application Pool**
3. Name: `Movex.API`
4. .NET Runtime: `.NET` (v8.0)
5. Managed pipeline mode: **Integrated**
6. Start app pool: ✓ (checked)
7. Click **OK**

**Option B: PowerShell**
```powershell
# Create app pool
New-IISAppPool -Name "Movex.API" -RuntimeVersion "8.0" -ManagedRuntimeVersion "" -Force

# Configure app pool recycling
$appPool = Get-IISAppPool "Movex.API"
$appPool.Recycling.PeriodicRestart.Time = [TimeSpan]::FromMinutes(60)  # Recycle hourly
$appPool.ProcessModel.MaxProcesses = 1  # Single worker process
$appPool | Set-IISAppPool
```

### 3.2 Create IIS Application/Site

**Option A: IIS Manager GUI**
1. Expand **Sites** → **Default Web Site** (or your site)
2. Right-click → **Add Application**
3. Alias: `movex-api`
4. Physical path: `C:\inetpub\wwwroot\movex-api`
5. Application pool: `Movex.API`
6. Click **OK**

**Option B: PowerShell**
```powershell
# Add application to Default Web Site
New-IISApplication -Site "Default Web Site" `
    -Name "movex-api" `
    -PhysicalPath "C:\inetpub\wwwroot\movex-api" `
    -ApplicationPool "Movex.API" `
    -Force
```

### 3.3 Configure Binding

Ensure your site has a binding:

**Example bindings:**
- HTTP: `http://SRXWEBAPP1:5000` (or your port)
- HTTPS: `https://SRXWEBAPP1:443` (recommended for production)

**In IIS Manager:**
1. **Sites** → **Default Web Site** (or your site)
2. Right-click → **Edit Bindings**
3. Add binding: Type=HTTP, IP=All, Port=5000, Host name=SRXWEBAPP1
4. (Optional) Add HTTPS binding if SSL cert is available

---

## Step 4: Environment Variables & Secrets

### 4.1 Set Environment Variables in IIS

**In IIS Manager:**
1. **Sites** → **Default Web Site** → **movex-api**
2. Right-click → **Manage Application** → **Advanced Settings**
3. Scroll to **Environment** section
4. Click **...** next to Environment
5. Add these variables:

| Variable | Value | Example |
|----------|-------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Production` |
| `M3_HOST` | M3 server IP/hostname | `150.3.2.100` |
| `M3_PORT` | M3 MI port | `6000` |
| `M3_USER` | M3 username | `MBUSER` |
| `M3_PASSWORD` | M3 password | `your-password` |
| `ApiKeys__Primary` | Generated API key | `q8VxK9...` |
| `Logging__LogLevel__Default` | Log level | `Information` |

**In PowerShell:**
```powershell
# Using IIS WebConfigurationManager
Import-Module WebAdministration

$appPath = "IIS:\Sites\Default Web Site\movex-api"

# Set environment variables
Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="ASPNETCORE_ENVIRONMENT"; value="Production"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="M3_HOST"; value="150.3.2.100"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="M3_PORT"; value="6000"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="M3_USER"; value="MBUSER"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="M3_PASSWORD"; value="your-password"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="ApiKeys__Primary"; value="your-generated-key"}

Set-WebConfigurationProperty -PSPath $appPath `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="Logging__LogLevel__Default"; value="Information"}
```

### 4.2 Verify Environment Variables

```powershell
# Check app pool environment
Get-ChildItem Env: | Where-Object { $_.Name -like "M3_*" -or $_.Name -like "ApiKeys*" }
```

---

## Step 5: Logging Setup

### 5.1 Create Logs Directory

```powershell
# Create logs folder (should already exist)
mkdir C:\inetpub\wwwroot\movex-api\logs -ErrorAction SilentlyContinue

# Verify permissions for app pool user
icacls "C:\inetpub\wwwroot\movex-api\logs" /grant "IIS AppPool\Movex.API:(OI)(CI)F"

# List files to verify rotation
dir "C:\inetpub\wwwroot\movex-api\logs"
```

### 5.2 Verify Logging Configuration

Check that `appsettings.json` has Serilog configured:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### 5.3 Monitor Logs

```powershell
# Check latest logs
Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 5

# Real-time log monitoring
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-$(Get-Date -Format 'yyyy-MM-dd').txt" -Tail 20 -Wait
```

---

## Step 6: Verification & Health Checks

### 6.1 Verify Application Started

```powershell
# Check if app pool is running
Get-IISAppPool "Movex.API" | Select-Object State, Name

# Start if stopped
Start-IISAppPool "Movex.API"

# Check IIS logs
Get-Content "C:\inetpub\logs\LogFiles\W3SVC1\u_ex*.log" -Tail 20
```

### 6.2 Test Health Endpoint

```powershell
# Test from SRXWEBAPP1 (or remote machine with network access)
$healthUrl = "http://SRXWEBAPP1:5000/health"
Invoke-WebRequest -Uri $healthUrl -UseBasicParsing

# Expected response:
# {
#   "status": "ok",
#   "timestamp": "2026-01-12T...",
#   "config": {
#     "host": "150.3.2.100",
#     "port": 6000,
#     "username": "***"
#   }
# }
```

### 6.3 Test API Key Authentication

```powershell
# Test without API key (should fail with 401)
$url = "http://SRXWEBAPP1:5000/api/programs"
Invoke-WebRequest -Uri $url -UseBasicParsing -ErrorAction Continue

# Test with API key (should return 200)
$headers = @{ "X-API-Key" = "your-api-key" }
Invoke-WebRequest -Uri $url -UseBasicParsing -Headers $headers
```

### 6.4 Test Sample Transaction

```powershell
$url = "http://SRXWEBAPP1:5000/api/workflows/StockManagement"
$headers = @{ 
    "X-API-Key" = "your-api-key"
    "Content-Type" = "application/json"
}
$body = @{
    ITNO = "DC210055"
    WHLO = "D01"
    WHSL = "D07AA5"
    STQI = "100"
} | ConvertTo-Json

Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body $body -UseBasicParsing
```

---

## Step 7: Production Hardening

### 7.1 Enable HTTPS

1. **Obtain SSL Certificate** (self-signed or from CA)
2. **Import to Windows Certificate Store**
   ```powershell
   Import-PfxCertificate -FilePath "C:\temp\cert.pfx" -CertStoreLocation Cert:\LocalMachine\My
   ```
3. **Bind to IIS Site**
   - IIS Manager → Sites → Bindings → Add → HTTPS on port 443

### 7.2 Set Logging Level

For production, use `Warning` to reduce log volume:

```powershell
Set-WebConfigurationProperty -PSPath "IIS:\Sites\Default Web Site\movex-api" `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="Logging__LogLevel__Default"; value="Warning"}
```

### 7.3 Configure App Pool Identity

```powershell
# Use custom app pool identity instead of ApplicationPoolIdentity
$appPool = Get-IISAppPool "Movex.API"
$appPool.ProcessModel.IdentityType = "SpecificUser"
$appPool.ProcessModel.UserName = "DOMAIN\serviceaccount"
$appPool.ProcessModel.Password = "password"
$appPool | Set-IISAppPool
```

### 7.4 Disable Debug Mode

Ensure `web.config` has debug disabled:

```xml
<compilation debug="false" />
```

### 7.5 Restrict File Permissions

```powershell
# Remove public access to config files
icacls "C:\inetpub\wwwroot\movex-api\appsettings.json" /inheritance:r /grant "Administrators:(F)" /grant "SYSTEM:(F)"
icacls "C:\inetpub\wwwroot\movex-api\transactions" /inheritance:r /grant "Administrators:(F)" /grant "SYSTEM:(F)"
```

---

## Step 8: Monitor and Maintain

### 8.1 Application Pool Recycling

Configure automatic app pool recycling:

```powershell
$appPool = Get-IISAppPool "Movex.API"
$appPool.Recycling.PeriodicRestart.Time = [TimeSpan]::FromMinutes(240)  # 4 hours
$appPool | Set-IISAppPool
```

### 8.2 Log Rotation

Logs auto-rotate daily. Monitor disk space:

```powershell
# Check log directory size
$logSize = (Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Log directory size: $logSize MB"

# Clean logs older than 30 days
Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs" -Filter "*.txt" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item
```

### 8.3 Performance Monitoring

```powershell
# Monitor app pool CPU and memory
Get-IISAppPool "Movex.API" | ForEach-Object {
    $procId = (Get-Process | Where-Object { $_.Name -eq "w3wp" }).Id
    Get-Process -Id $procId | Select-Object Name, CPU, @{Name="Memory(MB)"; Expression={$_.WorkingSet / 1MB}}
}
```

---

## Troubleshooting

### Issue: Application fails to start (HTTP 500)

**Solution:**
1. Check logs: `Get-Content "C:\inetpub\wwwroot\movex-api\logs\*.txt" -Tail 50`
2. Verify environment variables are set
3. Check M3 connectivity: `Test-NetConnection -ComputerName 150.3.2.100 -Port 6000`
4. Restart app pool: `Restart-IISAppPool -Name "Movex.API"`

### Issue: API key authentication fails (HTTP 401)

**Solution:**
1. Verify API key is set: `Get-WebConfigurationProperty -PSPath "IIS:\Sites\Default Web Site\movex-api" -Filter "system.webServer/aspNetCore/environmentVariables"`
2. Verify header is exactly: `X-API-Key: your-key` (case-sensitive)
3. Regenerate key if needed

### Issue: M3 connection fails

**Solution:**
1. Verify M3 host/port: `Test-NetConnection -ComputerName 150.3.2.100 -Port 6000`
2. Verify M3 credentials are correct
3. Check if M3 server is running
4. Check network firewall rules

### Issue: Logs not being written

**Solution:**
1. Verify logs directory exists and has write permissions
2. Check ASPNETCORE_ENVIRONMENT is "Production"
3. Verify appsettings.json has Serilog configured
4. Restart app pool to pick up environment variables

---

## Rollback Procedure

If deployment fails:

### Option 1: Restore Previous Version

```powershell
# Stop application
Stop-IISAppPool -Name "Movex.API"

# Backup current (broken) version
Move-Item "C:\inetpub\wwwroot\movex-api" "C:\inetpub\wwwroot\movex-api-broken-$(Get-Date -Format 'yyyyMMdd-HHmmss')" -Force

# Restore previous version
Copy-Item "C:\backups\movex-api-working" "C:\inetpub\wwwroot\movex-api" -Recurse

# Start application
Start-IISAppPool -Name "Movex.API"

# Verify health
Invoke-WebRequest -Uri "http://SRXWEBAPP1:5000/health"
```

### Option 2: Remove IIS Application

```powershell
# Remove application from IIS
Remove-IISApplication -Site "Default Web Site" -Name "movex-api" -Force

# Remove app pool
Remove-IISAppPool -Name "Movex.API" -Force

# Delete folder
Remove-Item "C:\inetpub\wwwroot\movex-api" -Recurse -Force
```

---

## Post-Deployment Checklist

- [ ] Health endpoint responds (`/health`)
- [ ] API key authentication works
- [ ] Can list programs (`/api/programs`)
- [ ] Can execute sample transaction
- [ ] Logs are being written to files
- [ ] App pool recycles on schedule without errors
- [ ] SSL/HTTPS is enabled (if required)
- [ ] Firewall rules allow access to M3 (port 6000)
- [ ] Backup of deployment created
- [ ] Team notified of successful deployment

---

## Support & Questions

For issues:
1. Check logs: `C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt`
2. Review [Troubleshooting section](#troubleshooting)
3. Enable Debug logging: Set `Logging__LogLevel__Default=Debug` temporarily
4. Contact DevOps team with logs and error details
