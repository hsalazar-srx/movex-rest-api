# SRXWEBAPP1 Deployment Checklist

**API:** Movex REST API (Movex.API)  
**Target:** SRXWEBAPP1 (Windows Server, IIS)  
**Runtime:** .NET 8.0  
**Date:** January 12, 2026  

---

## Phase 1: Pre-Deployment (Development Machine)

### 1.1 Code Verification
- [ ] All changes committed and pushed to repository
- [ ] Build succeeds: `dotnet build -c Release` → 0 errors
- [ ] All transaction configs present in `transactions/` folder:
  - [ ] `MMS200MI.json` (GetItmBasic, GetItmWhsBal)
  - [ ] `MMS310MI.json` (Update with BREM field at position 162)
  - [ ] `MMS003.json` (Get for cost retrieval)

### 1.2 Generate Release Build
```powershell
cd C:\Projects\MOVEX\API-Integration\movex-rest-api

# Clean previous builds
dotnet clean -c Release

# Publish Release configuration
dotnet publish -c Release -o "C:\temp\movex-api-release"
```
- [ ] Publish succeeds without errors
- [ ] `C:\temp\movex-api-release` contains ~45 MB of files
- [ ] Key files present:
  - [ ] `Movex.API.exe`
  - [ ] `appsettings.json`
  - [ ] `web.config` (in root)
  - [ ] `transactions/` folder with all configs

### 1.3 Generate API Key
```powershell
cd C:\Projects\MOVEX\API-Integration\movex-rest-api
.\scripts\generate-api-key-simple.ps1
```
- [ ] API key generated successfully
- [ ] Save key securely for deployment

### 1.4 Document Configuration
- [ ] M3 Host: `150.3.2.100` (or actual server)
- [ ] M3 Port: `6000` (or actual port)
- [ ] M3 User: `______` (verified with M3 admin)
- [ ] M3 Password: `______` (verified with M3 admin)
- [ ] API Key: `______` (generated above)
- [ ] Admin Key: `______` (generated or assigned)
- [ ] Swagger Enabled: `______` (recommended: false in prod; if true requires X-API-Key for /swagger)

### 1.5 Create Deployment Package
```powershell
# Zip the release folder
Compress-Archive -Path "C:\temp\movex-api-release" -DestinationPath "C:\temp\movex-api.zip"

# Copy to deployment location (USB, shared drive, etc.)
```
- [ ] Deployment package created: `movex-api.zip` (~20-30 MB)
- [ ] Package copied to accessible location

---

## Phase 2: Target Server Preparation (SRXWEBAPP1)

### 2.1 Run Pre-Deployment Verification
```powershell
# On SRXWEBAPP1, run the verification script
.\scripts\pre-deployment-check.ps1
```
- [ ] All critical checks pass (0 errors)
- [ ] Warnings reviewed and understood
- [ ] Network connectivity to M3 confirmed
- [ ] Disk space available (>500 MB)

### 2.2 Transfer Deployment Package
Choose one method:

**Method A: RDP Copy**
- [ ] Connect via RDP to SRXWEBAPP1
- [ ] Copy `movex-api.zip` to `C:\temp\`
- [ ] Extract: `Expand-Archive -Path "C:\temp\movex-api.zip" -DestinationPath "C:\inetpub\wwwroot\movex-api"`

**Method B: Network Share**
- [ ] Copy from dev machine to SRXWEBAPP1 shared drive

**Method C: Git Clone**
- [ ] Clone repository on server
- [ ] Run `dotnet publish -c Release`

- [ ] Files transferred successfully
- [ ] Verify: `dir C:\inetpub\wwwroot\movex-api` shows files

### 2.3 Create Backup of Existing (if applicable)
```powershell
# If previous version exists
Move-Item "C:\inetpub\wwwroot\movex-api" "C:\inetpub\wwwroot\movex-api-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```
- [ ] Previous version backed up (if applicable)
- [ ] Backup location documented: `_______________________`

---

## Phase 3: IIS Configuration (SRXWEBAPP1)

### 3.1 Run Deployment Script
```powershell
cd C:\inetpub\wwwroot\movex-api
.\scripts\deploy-to-iis.ps1 `
    -M3Host "150.3.2.100" `
    -M3Port "6000" `
    -M3User "MBUSER" `
    -M3Password "password" `
    -ApiKey "your-generated-key"
```
- [ ] Script runs without errors
- [ ] Application pool "Movex.API" created/updated
- [ ] IIS application configured
- [ ] Environment variables set
- [ ] Logs directory created with proper permissions

### 3.2 Manual Verification (if script method not available)
Follow [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) sections:
- [ ] Section 3: IIS Configuration (App Pool + Application)
- [ ] Section 4: Environment Variables & Secrets
- [ ] Section 5: Logging Setup

---

## Phase 4: Deployment Verification

### 4.1 Wait for App Startup
```powershell
# Wait 5-10 seconds for app to initialize
Start-Sleep -Seconds 10
```
- [ ] Waited for application startup

### 4.2 Test Health Endpoint
```powershell
$healthUrl = "http://SRXWEBAPP1:5000/health"
Invoke-WebRequest -Uri $healthUrl -UseBasicParsing

# Expected: StatusCode 200 with status="ok"
```
- [ ] Health endpoint returns HTTP 200
- [ ] Response contains `"status": "ok"`

### 4.3 Test API Key Authentication
```powershell
$url = "http://SRXWEBAPP1:5000/api/programs"
$headers = @{ "X-API-Key" = "your-api-key" }
Invoke-WebRequest -Uri $url -UseBasicParsing -Headers $headers

# Expected: StatusCode 200 with program list
```
- [ ] API returns program list (HTTP 200)
- [ ] At least MMS200MI, MMS310MI, MMS003 present

### 4.4 Test Validation Workflow
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

$response = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body $body -UseBasicParsing
$response.Content | ConvertFrom-Json
```
- [ ] Response contains `"success": true` or `"success": false` with errorCode
- [ ] Validation logic working correctly

### 4.5 Verify Logging
```powershell
# Check that logs are being written
Get-ChildItem "C:\inetpub\wwwroot\movex-api\logs" -Filter "*.txt" | Select-Object -First 1

# View latest entries
Get-Content "C:\inetpub\wwwroot\movex-api\logs\movex-api-$(Get-Date -Format 'yyyy-MM-dd').txt" -Tail 20
```
- [ ] Log files exist in `logs/` directory
- [ ] Logs contain recent entries (timestamps match deployment time)
- [ ] No ERROR entries visible in logs

---

## Phase 5: Hardening & Optimization

### 5.1 Configure Logging Level
```powershell
# Set to Warning for production (reduces log volume)
Set-WebConfigurationProperty -PSPath "IIS:\Sites\Default Web Site\movex-api" `
    -Filter "system.webServer/aspNetCore/environmentVariables" `
    -Value @{name="Logging__LogLevel__Default"; value="Warning"}
```
- [ ] Log level set to appropriate level (Warning or Information)
- [ ] App pool restarted to apply setting

### 5.2 Enable HTTPS (if certificate available)
1. [ ] SSL certificate obtained/imported
2. [ ] HTTPS binding added to IIS site
3. [ ] Test: `https://SRXWEBAPP1/movex-api/health`

### 5.3 Configure App Pool Recycling
- [ ] App pool recycling interval set to 240 minutes (4 hours)
- [ ] Max process count set to 1
- [ ] Tested graceful restart

---

## Phase 6: Post-Deployment Validation

### 6.1 Load Balancer / DNS (if applicable)
- [ ] Update load balancer with SRXWEBAPP1 endpoint
- [ ] Update DNS records (if needed)
- [ ] Test from external client: `curl http://SRXWEBAPP1:5000/health`

### 6.2 Monitoring Setup
- [ ] Logs monitored for errors
- [ ] App pool status monitored
- [ ] M3 connectivity monitored
- [ ] Health endpoint on monitoring dashboard

### 6.3 Documentation Updates
- [ ] Deployment date and time recorded: `_______________________`
- [ ] Deployed version recorded: `_______ (release build)`
- [ ] Configuration documented in secure location
- [ ] Team notified of deployment completion

---

## Phase 7: Rollback Plan (if needed)

### 7.1 Rollback to Previous Version
```powershell
# Stop application
Stop-IISAppPool -Name "Movex.API"

# Restore from backup
Remove-Item "C:\inetpub\wwwroot\movex-api" -Recurse -Force
Copy-Item "C:\inetpub\wwwroot\movex-api-backup-*" "C:\inetpub\wwwroot\movex-api" -Recurse

# Start application
Start-IISAppPool -Name "Movex.API"

# Verify health
Invoke-WebRequest -Uri "http://SRXWEBAPP1:5000/health" -UseBasicParsing
```
- [ ] Backup location: `_______________________`
- [ ] Rollback procedure documented and tested

### 7.2 Emergency Contacts
- [ ] M3 Administrator: `_______________________`
- [ ] Infrastructure Team: `_______________________`
- [ ] DevOps Lead: `_______________________`

---

## Troubleshooting Quick Links

- **IIS Configuration Issues:** [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) - Troubleshooting section
- **Logging Issues:** [LOGGING_SETUP.md](LOGGING_SETUP.md) - File Logging Issues section
- **General API Issues:** [README.md](README.md) - Troubleshooting section
- **Pre-Deployment Failures:** Re-run `scripts/pre-deployment-check.ps1`

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Developer | _________________ | ___/___/_____ | _________________ |
| DevOps | _________________ | ___/___/_____ | _________________ |
| Infrastructure | _________________ | ___/___/_____ | _________________ |
| QA/Tester | _________________ | ___/___/_____ | _________________ |

---

## Notes & Observations

```
[Space for deployment notes, issues encountered, and resolutions]

_________________________________________________________________

_________________________________________________________________

_________________________________________________________________

_________________________________________________________________
```

---

---

## Phase 6: IIS Parity Validation (cloud/dev-prod-parity v1.0.0)

Run before every UAT/production deployment. These gaps are invisible in development (Kestrel)
but break in IIS. Reference: `c:/Projects/Knowledge-Management/vault/runbooks/iis-deployment.md`
Agent: `@validator-iis-deploy`

**Pre-deploy:**
- [ ] Controller `[Route(...)]` attributes do NOT include the IIS sub-app path prefix
      (IIS strips the prefix before forwarding — including it causes 404 on every route)
- [ ] All required secrets present in `appsettings.Production.json`
      (missing key → 502 Bad Gateway at runtime with no obvious UI error)
- [ ] App pool `managedRuntimeVersion` = `""` (No Managed Code), `loadUserProfile` = `true`
- [ ] IIS URL Rewrite Module installed (if SPA frontend present)
      `Get-WebGlobalModule -Name "RewriteModule"` — empty output = NOT installed

**Deploy:**
- [ ] Identify correct app pool: `appcmd.exe list app /site.name:"<SiteName>"`
- [ ] Stop correct app pool before replacing DLL: `appcmd.exe stop apppool <PoolName>`
- [ ] Verify DLL timestamp after copy matches build output timestamp
- [ ] Start app pool: `appcmd.exe start apppool <PoolName>`

**Post-deploy smoke tests:**
- [ ] `curl.exe --negotiate -u : http://localhost/<path>/health` → 401 then 200 (not 404)
      404 = route mismatch; 401 loop = auth config issue
- [ ] Check IIS event log for startup errors
- [ ] Check stdout log (`.\logs\stdout*.log`) if available

---

**Deployment Status:** ☐ IN PROGRESS  ☐ COMPLETED  ☐ FAILED  ☐ ROLLED BACK

**Completion Date:** ___/___/_____
