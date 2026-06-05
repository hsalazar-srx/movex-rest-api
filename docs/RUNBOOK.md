# Movex API Runbook (SRXWEBAPP1)

**Target Server:** SRXWEBAPP1 (IIS, Windows Server)  
**Runtime:** .NET 8.0  
**Primary Docs:** DEPLOYMENT_READY.md, DEPLOYMENT_CHECKLIST.md, IIS_DEPLOYMENT_SRXWEBAPP1.md, LOGGING_SETUP.md  

---

## 1) Pre-requisites on SRXWEBAPP1
- Windows Server 2019+/IIS 10 with ASP.NET Core Hosting Bundle
- .NET 8.0 Runtime installed (`dotnet --list-runtimes` shows 8.0.x)
- Network access to M3: `150.3.2.100:6000`
- Disk: ≥ 500 MB free; Ports: HTTP 5000 (or your binding)

## 2) Configuration to collect
- `M3_HOST`, `M3_PORT`, `M3_USER`, `M3_PASSWORD` (company 300 assumed)
- API keys: `ApiKeys__Primary`, `ApiKeys__Admin`
- (Optional) `AllowedOrigins` for CORS
- Logging level: `Logging__LogLevel__Default` (Information/Warning)
- Swagger: `Swagger__Enabled` (set to true only if needed; requires `X-API-Key` in production)
- **DB2 direct-query** (for `/api/mitpop/search` and `/api/mitmas/next-sequence`):
  - `Db2__ConnectionString` — IBM i ODBC connection string. Set via user-secrets (dev) or IIS env var (prod).
    Format: `Driver={IBM i Access ODBC Driver};System=<IP>;Uid=<user>;Pwd=<pass>;`
  - `Db2__SchemaCmp100` — schema for CONO 100 (default `MVXCDTA`)
  - `Db2__SchemaCmp300` — schema for CONO 300 (default `MVXC300`)
  - **Prerequisite:** IBM i Access ODBC Driver must be installed on SRXWEBAPP1 before these endpoints can serve requests.

## 3) Build & package (development machine)
```powershell
cd C:\Projects\MOVEX\API-Integration\movex-rest-api

# Clean and publish
dotnet clean -c Release
dotnet publish -c Release -o C:\temp\movex-api-release

# Package
Compress-Archive -Path C:\temp\movex-api-release -DestinationPath C:\temp\movex-api.zip
```

## 4) Transfer to SRXWEBAPP1
- Copy `C:\temp\movex-api.zip` to `C:\temp\` on SRXWEBAPP1 (RDP/share).

## 5) Pre-deployment checks (on SRXWEBAPP1)
```powershell
cd C:\temp
Expand-Archive .\movex-api.zip -DestinationPath C:\inetpub\wwwroot\movex-api
cd C:\inetpub\wwwroot\movex-api

# Verify server readiness
.\scripts\pre-deployment-check.ps1
```

## 6) Deploy to IIS (automated)
```powershell
cd C:\inetpub\wwwroot\movex-api
.\scripts\deploy-to-iis.ps1 `
  -M3Host "150.3.2.100" `
  -M3Port "6000" `
  -M3User "MBUSER" `
  -M3Password "<password>" `
  -ApiKey "<primary-api-key>" `
  -AdminKey "<admin-api-key>" `
  -SiteName "Default Web Site" `
  -AppPath "/movex-api" `
  -AppPoolName "Movex.API"
```
If scripting is unavailable, follow IIS steps in `IIS_DEPLOYMENT_SRXWEBAPP1.md` (App Pool, Application, Env Vars).

## 7) Verify
- Health: `curl http://SRXWEBAPP1:5000/health`
- Programs: `curl -H "X-API-Key:<key>" http://SRXWEBAPP1:5000/api/programs`
- Stock Management:
```bash
curl -X POST http://SRXWEBAPP1:5000/api/workflows/StockManagement \
  -H "X-API-Key: <key>" -H "Content-Type: application/json" \
  -d '{"ITNO":"DC210055","WHLO":"D01","WHSL":"D07AA5","STQI":"100"}'
```
- Standard Cost:
```bash
curl -X POST http://SRXWEBAPP1:5000/api/workflows/GetStandardCost \
  -H "X-API-Key: <key>" -H "Content-Type: application/json" \
  -d '{"ITNO":"DC210055","FACI":"D01"}'
```

## 8) Logs
- Location: `C:\inetpub\wwwroot\movex-api\logs\movex-api-YYYY-MM-DD.txt`
- Rotation: Daily, 30-day retention (Serilog) 
- Adjust level: set `Logging__LogLevel__Default` (e.g., Warning for prod)

## 9) Post-deployment checks
- App pool running: `Get-IISAppPool "Movex.API"`
- No errors in latest log file
- Health and sample transactions return 200/expected payloads

## 10) Rollback (simple)
- Stop app pool `Movex.API`
- Restore previous backup folder
- Start app pool and re-test health

## 11) References
- DEPLOYMENT_CHECKLIST.md (step-by-step with checkboxes)
- DEPLOYMENT_READY.md (executive summary)
- IIS_DEPLOYMENT_SRXWEBAPP1.md (detailed IIS guide)
- LOGGING_SETUP.md (logging config/troubleshooting)
- README.md (API usage/troubleshooting)
