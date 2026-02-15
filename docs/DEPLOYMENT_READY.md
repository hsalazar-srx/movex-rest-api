# Deployment to SRXWEBAPP1 - Ready for Production

**Status:** ✅ Ready for Deployment  
**Date Prepared:** January 12, 2026  
**API Version:** Movex.API v1.0.0  
**Target Platform:** Windows Server + IIS (.NET 8.0)  
**Build Configuration:** Release (0 errors)

---

## 📋 What's Included

This deployment package contains a complete, production-ready Movex REST API with:

### ✅ Core Features
- **Multi-program config-driven architecture** - Add M3 programs via JSON (no code changes)
- **Service layer validation** - Shared business logic validation (ItemValidationService)
- **MES workflows** - Stock Management, Cost Retrieval, Stock Adjustment endpoints
- **Connection pooling** - Reuses authenticated M3 socket connections
- **Async/await throughout** - Non-blocking I/O for high concurrency
- **Hot-reload config** - FileSystemWatcher detects config changes automatically
- **Polly resilience** - Retry + circuit breaker for M3 failover protection
- **File logging** - Serilog with rotating logs, 30-day retention

### ✅ Deployment Assets
- **IIS_DEPLOYMENT_SRXWEBAPP1.md** - Complete step-by-step IIS deployment guide
- **DEPLOYMENT_CHECKLIST.md** - Pre-deployment, deployment, and post-deployment checklist
- **web.config** - IIS/ASP.NET Core hosting configuration
- **scripts/pre-deployment-check.ps1** - Verify prerequisites on SRXWEBAPP1
- **scripts/deploy-to-iis.ps1** - Automated IIS configuration and deployment
- **LOGGING_SETUP.md** - File logging configuration and troubleshooting
- **README.md** - Complete API documentation with examples

### ✅ Configured M3 Programs
1. **MMS200MI** (Item Master Management)
   - GetItmBasic: Item validation (STAT, ITTY)
   - GetItmWhsBal: Warehouse balance checking
   
2. **MMS310MI** (Stock Adjustment)
   - Update: Stock quantity adjustment with BREM field at MI position 162
   
3. **MMS003** (Cost Management)
   - Get: Standard cost price retrieval by facility

### ✅ Pre-Built Workflows
1. **POST /api/workflows/StockManagement**
   - Validates item → Checks warehouse balance → Updates stock
   - Returns structured error codes (001-004)

2. **POST /api/workflows/GetStandardCost**
   - Validates item → Retrieves unit cost price
   - Returns error codes (001-003, 011)

3. **POST /api/workflows/AdjustStockWithValidation** (legacy)
   - Validates item → Updates stock
   - Simpler workflow without balance checking

---

## 🚀 Quick Deployment Steps

### Step 1: On Your Development Machine
```powershell
cd C:\Projects\MOVEX\API-Integration\dotnet-poc

# Generate Release build
dotnet publish -c Release -o "C:\temp\movex-api-release"

# Create deployment package
Compress-Archive -Path "C:\temp\movex-api-release" -DestinationPath "C:\temp\movex-api.zip"
```

### Step 2: Transfer to SRXWEBAPP1
- Copy `movex-api.zip` to SRXWEBAPP1 via RDP, network share, or git

### Step 3: On SRXWEBAPP1
```powershell
# Extract files
Expand-Archive -Path "C:\temp\movex-api.zip" -DestinationPath "C:\inetpub\wwwroot\movex-api"

# Verify prerequisites
.\scripts\pre-deployment-check.ps1

# Deploy to IIS (configure with your values)
.\scripts\deploy-to-iis.ps1 `
    -M3Host "150.3.2.100" `
    -M3Port "6000" `
    -M3User "MBUSER" `
    -M3Password "your-password" `
    -ApiKey "your-generated-key"

# Test health endpoint
Invoke-WebRequest -Uri "http://SRXWEBAPP1:5000/health" -UseBasicParsing
```

### Step 4: Follow Checklist
- Use **DEPLOYMENT_CHECKLIST.md** for complete step-by-step process
- Follow **IIS_DEPLOYMENT_SRXWEBAPP1.md** for detailed IIS configuration

---

## 📖 Documentation Overview

### For Deployment Engineers
1. **Start here:** [DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md)
   - 7-phase deployment process with checkboxes
   - Pre-deployment verification
   - IIS configuration
   - Post-deployment validation
   - Rollback procedure

2. **Detailed reference:** [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md)
   - 8 detailed deployment steps with examples
   - Pre-deployment prerequisites
   - IIS app pool and application configuration
   - Environment variables setup
   - Logging directory configuration
   - Health checks and verification
   - Production hardening
   - Troubleshooting guide with solutions

### For Operations & Support
1. **[LOGGING_SETUP.md](LOGGING_SETUP.md)**
   - Log file locations and naming convention
   - 30-day retention policy
   - Log level configuration
   - Viewing and searching logs
   - Troubleshooting logging issues

2. **[README.md](README.md) - Troubleshooting section**
   - Common errors and solutions
   - Log file locations for different OSes
   - How to view and search logs
   - Log level legend

### For API Users
1. **[README.md](README.md) - Full API documentation**
   - All endpoints with examples
   - Authentication and API keys
   - Request/response formats
   - Error codes and meanings
   - Business workflow descriptions

---

## 🔧 Configuration Requirements

### M3 Connection (set via IIS Application Settings)
```
M3_HOST        = 150.3.2.100 (M3 server IP/hostname)
M3_PORT        = 6000 (MI socket port)
M3_USER        = MBUSER (or your M3 user)
M3_PASSWORD    = *** (M3 user password)
```

### API Security (set via IIS Application Settings)
```
ApiKeys__Primary    = your-generated-api-key (256-bit Base64)
ApiKeys__Admin      = your-admin-key (for key rotation)
```

### Logging (set via IIS Application Settings)
```
Logging__LogLevel__Default                = Information (or Warning for production)
ASPNETCORE_ENVIRONMENT                    = Production
Swagger__Enabled                           = false (set true only when needed)
```

---

## 🧪 Deployment Testing

### Health Check
```bash
curl http://SRXWEBAPP1:5000/health
# Returns: { "status": "ok", "timestamp": "...", "config": {...} }
```

### List Programs
```bash
curl -H "X-API-Key: your-api-key" http://SRXWEBAPP1:5000/api/programs
# Returns: ["MMS200MI", "MMS310MI", "MMS003"]
```

### Test Stock Management Workflow
```bash
curl -X POST http://SRXWEBAPP1:5000/api/workflows/StockManagement \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "ITNO": "DC210055",
    "WHLO": "D01",
    "WHSL": "D07AA5",
    "STQI": "100"
  }'
```

---

## 📊 System Requirements

| Component | Requirement | Notes |
|-----------|-------------|-------|
| **OS** | Windows Server 2019+ | Or Windows 10/11 Pro |
| **.NET Runtime** | 8.0+ | Download from dotnet.microsoft.com |
| **IIS** | 10.0+ | With ASP.NET Core Hosting Bundle |
| **Disk Space** | 500 MB minimum | For app + logs |
| **RAM** | 2 GB minimum | App pool typically uses 100-200 MB |
| **Network** | Connectivity to M3 on port 6000 | Required for all transactions |

---

## 🔒 Security Checklist

- [ ] HTTPS enabled (SSL certificate configured)
- [ ] API keys stored securely (not in git or version control)
- [ ] M3 credentials stored securely (IIS Application Settings only)
- [ ] Log files restricted from public access
- [ ] Configuration files (appsettings.json) restricted from public access
- [ ] Swagger disabled by default in production, or protected with API key access
- [ ] Only necessary ports exposed (5000 for HTTP, 443 for HTTPS)
- [ ] App pool identity uses ApplicationPoolIdentity (least privilege)
- [ ] Regular log file monitoring for security events

---

## 📞 Support & Troubleshooting

### If Deployment Fails
1. Check logs: `C:\inetpub\wwwroot\movex-api\logs\movex-api-*.txt`
2. Review [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) - Troubleshooting section
3. Run pre-deployment check again: `.\scripts\pre-deployment-check.ps1`
4. Verify M3 connectivity: `Test-NetConnection -ComputerName 150.3.2.100 -Port 6000`

### If Health Check Fails
1. Wait 10 seconds for app startup
2. Check app pool state: `Get-IISAppPool "Movex.API" | Select-Object State`
3. Check logs for error details
4. Restart app pool: `Restart-IISAppPool -Name "Movex.API"`
5. Verify environment variables are set correctly

### If Transactions Fail
1. Test M3 connectivity directly
2. Verify M3 credentials are correct
3. Check M3 user has required transaction permissions
4. Review transaction config in `transactions/` folder
5. Enable Debug logging: Set `Logging__LogLevel__Default=Debug`

---

## 📝 Deployment Record

```
Deployment Date:        _________________
Deployed By:            _________________
Version:                movex-api-release (2026-01-12)
Build Configuration:    Release
.NET Runtime:           8.0.x
Target Server:          SRXWEBAPP1
IIS Site:               Default Web Site
Application:            /movex-api
App Pool:               Movex.API
Health Endpoint:        http://SRXWEBAPP1:5000/health
API Endpoint:           http://SRXWEBAPP1:5000/api
Status:                 ☐ Pending  ☐ In Progress  ☐ Completed  ☐ Rolled Back

Issues Encountered:     _________________________________________________

Resolution:             _________________________________________________

Sign-off:               _________________  Date: _________________
```

---

## 🎯 Next Steps

1. **Before Deployment**
   - [ ] Obtain M3 credentials
   - [ ] Generate API key: `.\scripts\generate-api-key-simple.ps1`
   - [ ] Reserve SRXWEBAPP1 and schedule deployment window

2. **During Deployment**
   - [ ] Follow DEPLOYMENT_CHECKLIST.md
   - [ ] Have [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) open for reference
   - [ ] Have team member on standby for rollback if needed

3. **After Deployment**
   - [ ] Run verification tests in checklist
   - [ ] Monitor logs for errors
   - [ ] Update monitoring dashboard with SRXWEBAPP1 endpoint
   - [ ] Notify team of successful deployment

---

## 📚 Files in This Package

```
dotnet-poc/
├── Movex.API.csproj                    # .NET project file
├── Program.cs                          # ASP.NET Core startup
├── web.config                          # IIS hosting configuration
├── appsettings.json                    # Default settings
├── README.md                           # Complete API documentation
├── LOGGING_SETUP.md                    # Logging configuration guide
├── IIS_DEPLOYMENT_SRXWEBAPP1.md        # Detailed IIS deployment steps
├── DEPLOYMENT_CHECKLIST.md             # Deployment process checklist
├── Controllers/
│   └── TransactionController.cs        # REST API endpoints
├── Services/
│   ├── TransactionService.cs           # Transaction orchestration
│   ├── ItemValidationService.cs        # Business validation
│   └── M3ResponseParser.cs             # Response parsing & type conversion
├── Core/
│   ├── Interfaces/                     # Service contracts
│   └── Models/
├── Infrastructure/
│   ├── Configuration/                  # Config loading & validation
│   ├── ConnectionPool/                 # M3 socket pooling
│   ├── M3Protocol/                     # Socket protocol wrapper
│   └── Resilience/                     # Polly policies
├── transactions/                       # M3 program configs
│   ├── MMS200MI.json                   # Item Master Management
│   ├── MMS310MI.json                   # Stock Adjustment
│   └── MMS003.json                     # Cost Management
└── scripts/
    ├── pre-deployment-check.ps1        # Prerequisites verification
    └── deploy-to-iis.ps1               # Automated IIS deployment
```

---

## ✅ Deployment Ready Confirmation

This package has been verified and is ready for production deployment:

- ✅ Code compiles without errors (Release configuration)
- ✅ All transaction configs are present and validated
- ✅ IIS deployment automation scripts tested
- ✅ Pre-deployment verification script tested
- ✅ Logging configured with Serilog file rotation
- ✅ API key generation script ready
- ✅ Complete documentation provided
- ✅ Health check endpoint functional
- ✅ All workflows tested and working
- ✅ Troubleshooting guide included
- ✅ Rollback procedure documented

**Status:** 🟢 **READY FOR DEPLOYMENT TO SRXWEBAPP1**

---

For questions or issues during deployment, refer to:
1. [DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md) - Follow this first
2. [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) - Detailed reference
3. [README.md](README.md) - API documentation and troubleshooting
4. [LOGGING_SETUP.md](LOGGING_SETUP.md) - Logging issues
