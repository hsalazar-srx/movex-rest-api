# Movex REST API - .NET (Config-Driven Architecture)

ASP.NET Core REST API for M3 MOVEX using a pure managed socket client (no mvxsock.dll required). 
**Config-driven architecture** supports multiple M3 programs through JSON configuration files.

Tested on .NET 8.

## 📋 Workspace Standards Compliance

**IMPORTANT:** This project MUST comply with **[WORKSPACE_RULES.md](../../../.github/WORKSPACE_RULES.md)**.

**Key Requirements:**
- ✅ Future: Audit logs to **SQL Server** (currently file-based, migration planned)
- ✅ Connection strings in **User Secrets** (development) or **environment variables** (IIS)
- ⚠️ **Network security:** Current M3 sockets use plain TCP; deploy behind TLS 1.2+ (e.g., VPN, TLS terminator, or gateway). Native in-app TLS (`SslStream`) support is planned.
- ✅ PascalCase for C# classes, camelCase for variables
- ⚠️ Action Required: Migrate to standard SQL Server audit schema

**Validation:** Run `../../../.github/scripts/validate-workspace-compliance.ps1` before deployment.

---

## Features

✅ **Multi-Program Support** - Add new M3 programs via JSON config (no code changes)  
✅ **Async/Await Stack** - Fully non-blocking I/O for high concurrency  
✅ **Connection Pooling** - Reuse authenticated connections for performance  
✅ **Response Parsing** - Automatic type conversion (numeric, date, boolean, text)  
✅ **Hot Reload** - Config changes detected automatically via FileSystemWatcher  
✅ **Clean Architecture** - Separated concerns (validation, building, orchestration)  
✅ **Dependency Injection** - All services use interfaces for testability  
✅ **Resilience** - Polly-based retry + circuit breaker for M3 failover protection  
✅ **Service Layer Validation** - Shared business logic validation (IItemValidationService)  
✅ **MES Workflows** - Pre-built stock management and cost retrieval workflows  
✅ **File Logging** - Rotating log files with configurable levels (logs/ directory)  

## 📈 Diagrams

Up-to-date Mermaid diagrams documenting the codebase live in `docs/diagrams/`:

- Architecture overview: [docs/diagrams/architecture.md](docs/diagrams/architecture.md)
- End-to-end transaction sequence: [docs/diagrams/sequence-transaction.md](docs/diagrams/sequence-transaction.md)
- API key authentication flow: [docs/diagrams/auth-flow.md](docs/diagrams/auth-flow.md)
- Transaction configuration loading: [docs/diagrams/config-flow.md](docs/diagrams/config-flow.md)
- Connection pool lifecycle: [docs/diagrams/connection-pool.md](docs/diagrams/connection-pool.md)
- Deployment topology (SRXWEBAPP1/IIS): [docs/diagrams/deployment-topology.md](docs/diagrams/deployment-topology.md)
- Response parsing behavior: [docs/diagrams/response-parsing.md](docs/diagrams/response-parsing.md)

Tip: In VS Code or GitHub, Mermaid blocks render natively; you can also use a Mermaid preview extension.

## 🚀 Deployment

For production deployment to **SRXWEBAPP1 (IIS)**:

See [IIS_DEPLOYMENT_SRXWEBAPP1.md](IIS_DEPLOYMENT_SRXWEBAPP1.md) for complete step-by-step instructions including:
- Pre-deployment verification checklist
- IIS configuration
- Environment variables setup
- Logging directory configuration
- Health checks and testing
- Troubleshooting and rollback

For day-to-day operations, see the runbook: [docs/RUNBOOK.md](docs/RUNBOOK.md).

**Quick deployment script:**
```powershell
# On SRXWEBAPP1, after files are transferred:
.\scripts\pre-deployment-check.ps1          # Verify prerequisites
.\scripts\deploy-to-iis.ps1 -M3Host "150.3.2.100" -M3Port "6000" -M3User "MBUSER" -ApiKey "your-key"
```

## Prerequisites

- .NET 8.0 SDK or later (for development)
- .NET 8.0 Runtime (for production on SRXWEBAPP1)
- Network access to the M3 MI server (host/port)
- IIS 10.0+ with ASP.NET Core Hosting Bundle (for SRXWEBAPP1)

## Development Environment Setup

### 1. Clone and Install Dependencies

```powershell
# Clone or navigate to project
cd C:\Projects\MOVEX\API-Integration\movex-rest-api

# Restore NuGet packages
dotnet restore

# Verify installation
dotnet --version  # Should show 8.0.x or later
```

### 2. Configure Development Secrets

Use **User Secrets** to avoid committing credentials to source control:

```powershell
# Initialize user secrets (creates a unique ID for this project)
dotnet user-secrets init

# Set M3 connection details
dotnet user-secrets set "M3:HOST" "150.3.2.100"
dotnet user-secrets set "M3:PORT" "6300"
dotnet user-secrets set "M3:USER" "MBUSER"
dotnet user-secrets set "M3:PASSWORD" "your-password"
dotnet user-secrets set "M3:DEFAULT_COMPANY" "300"

# Generate and set API keys
.\scripts\generate-api-key-simple.ps1  # Auto-generates and stores ApiKeys:Primary
dotnet user-secrets set "ApiKeys:Admin" "admin-key-for-testing"

# Verify secrets are stored
dotnet user-secrets list
```

**Expected Output:**
```
ApiKeys:Primary = <generated-key>
ApiKeys:Admin = admin-key-for-testing
M3:HOST = 150.3.2.100
M3:PORT = 6300
M3:USER = MBUSER
M3:PASSWORD = ********
M3:DEFAULT_COMPANY = 300
```

### 3. Configure Transaction Definitions

Transaction configurations are stored in `transactions/` directory. No changes needed for initial setup—**MMS175MI**, **MMS200MI**, **MMS310MI**, and **MMS003** are pre-configured.

To add new transactions, create/edit JSON files in `transactions/`:

```json
{
  "program": "MMS200MI",
  "library": "MVXCOBJ",
  "description": "Item Master Management",
  "transactions": [
    {
      "name": "GetItmBasic",
      "description": "Get Item Basic Information",
      "httpMethod": "POST",
      "fields": [ /* input fields */ ],
      "responseFields": [ /* output fields */ ]
    }
  ]
}
```

Changes are **hot-reloaded** automatically—no restart required.

### 4. Build the Project

```powershell
# Build and check for errors
dotnet build

# Expected output:
# Build succeeded.
#     0 Warning(s)
#     0 Error(s)
```

### 5. Run the API

```powershell
# Start the development server
dotnet run

# Server starts at: http://localhost:5000
# Swagger UI: http://localhost:5000/swagger
```

**Expected Console Output:**
```
[INFO] Movex.API starting up...
[INFO] Configuration loaded from transactions/MMS200MI.json
[INFO] Configuration loaded from transactions/MMS310MI.json
[INFO] Connection pool initialized: MinSize=2, MaxSize=10
[INFO] Now listening on: http://localhost:5000
[INFO] Application started. Press Ctrl+C to shut down.
```

### OpenAPI / Swagger

Swagger is enabled in all environments:

- UI: `http://localhost:5000/swagger`
- JSON: `http://localhost:5000/swagger/v1/swagger.json`

The committed spec lives at `docs/openapi.json`. To update it after API changes, run:

```powershell
./scripts/generate-openapi.ps1
```

**Windows runtime requirement:** the Swashbuckle CLI (`swagger.exe`) targets **.NET 7.0**. If you only have .NET 6/8/9 installed, install the **.NET 7.0 runtime (x64)** before running the script.

See [docs/OPENAPI.md](docs/OPENAPI.md) for details.

### 6. Test the API

#### Option A: Using PowerShell (Quick Test)

```powershell
# Get your API key from secrets
$apiKey = dotnet user-secrets list | Select-String "ApiKeys:Primary" | ForEach-Object { ($_ -split "=")[1].Trim() }

# Test health endpoint (no auth required)
Invoke-RestMethod -Uri "http://localhost:5000/health"

# Test transaction endpoint
$headers = @{ "X-API-Key" = $apiKey }
$body = @{
    CONO = "300"
    ITNO = "DC210055"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/api/MMS200MI/GetItmBasic" `
    -Method Post `
    -Headers $headers `
    -Body $body `
    -ContentType "application/json"
```

**Expected Response:**
```json
{
  "CONO": "300",
  "ITNO": "DC210055",
  "ITDS": "Item Description",
  "STAT": 20,
  "ITTY": "030"
}
```

#### Option B: Using Postman

1. **Import Collection** (if available in `postman-collections/`)
2. **Create Environment:**
   - `baseUrl`: `http://localhost:5000`
   - `apiKey`: `<your-key-from-user-secrets>`
3. **Test Endpoints:**
   - GET `/health` — No auth required
   - POST `/api/MMS200MI/GetItmBasic` — With X-API-Key header
   - POST `/api/workflows/StockManagement` — Stock adjustment workflow

#### Option C: Using curl

```bash
# Health check
curl http://localhost:5000/health

# Transaction request (replace API_KEY)
curl -X POST http://localhost:5000/api/MMS200MI/GetItmBasic \
  -H "X-API-Key: YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"CONO":"300","ITNO":"DC210055"}'
```

### 7. Debugging in Visual Studio / VS Code

#### Visual Studio:
1. Open `Movex.API.csproj`
2. Set breakpoints in controllers/services
3. Press **F5** to start debugging
4. API starts with debugger attached

#### VS Code:
1. Open project folder
2. Install **C# Dev Kit** extension
3. Press **F5** → Select ".NET Core Launch (web)"
4. Set breakpoints and debug

### 8. View Logs

Logs are written to `logs/` directory:

```powershell
# View today's log file
Get-Content logs/movex-api-$(Get-Date -Format yyyy-MM-dd).txt -Tail 50 -Wait

# Search for errors
Get-Content logs/*.txt | Select-String "ERROR"
```

### 9. Monitor Connection Pool

Check pool statistics via dedicated endpoint:

```powershell
$apiKey = dotnet user-secrets list | Select-String "ApiKeys:Primary" | ForEach-Object { ($_ -split "=")[1].Trim() }

Invoke-RestMethod -Uri "http://localhost:5000/api/transactions/pool-stats" `
    -Headers @{ "X-API-Key" = $apiKey }
```

**Expected Response:**
```json
{
  "totalConnections": 5,
  "availableConnections": 3,
  "inUseConnections": 2,
  "minPoolSize": 2,
  "maxPoolSize": 10
}
```

### 10. Common Development Tasks

#### Add a New Transaction

1. Edit `transactions/MMS200MI.json` (or create new program JSON)
2. Add transaction definition with fields and responseFields
3. Save file → API auto-reloads configuration (no restart)
4. Test new endpoint: `POST /api/{program}/{transaction}`

#### Change Log Level

```powershell
# Edit appsettings.json temporarily
# Or set via environment variable:
$env:Logging__LogLevel__Default = "Debug"
dotnet run
```

#### Rebuild After Code Changes

```powershell
# Stop server (Ctrl+C)
dotnet build
dotnet run
```

#### Clear User Secrets (Start Fresh)

```powershell
# Remove all secrets
dotnet user-secrets clear

# Re-add required secrets
dotnet user-secrets set "M3:HOST" "150.3.2.100"
dotnet user-secrets set "M3:PORT" "6300"
# ... etc
```

#### Switch Between Development and Production Environments

ASP.NET Core uses the `ASPNETCORE_ENVIRONMENT` variable to determine which configuration files to load and which features to enable.

**Check Current Environment:**

```powershell
# View current environment setting
$env:ASPNETCORE_ENVIRONMENT

# If empty, defaults to "Production"
```

**Run in Development Mode (default for `dotnet run`):**

```powershell
# Method 1: Set environment variable temporarily (current session only)
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run

# Method 2: Set via command line (Linux/Mac)
# ASPNETCORE_ENVIRONMENT=Development dotnet run
```

**Development mode enables:**
- User Secrets loading (from `~/.microsoft/usersecrets/`)
- Detailed error pages with stack traces
- Hot reload for configuration changes
- `appsettings.Development.json` overrides

**Run in Production Mode:**

```powershell
# Method 1: Set environment variable
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run

# Method 2: Unset the variable (defaults to Production)
Remove-Item Env:\ASPNETCORE_ENVIRONMENT
dotnet run

# Method 3: Set permanently for current user (persists across sessions)
[System.Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production", "User")
```

**Production mode enables:**
- Environment variables for secrets (User Secrets NOT loaded)
- Generic error messages (no stack traces)
- `appsettings.Production.json` overrides
- Performance optimizations

**Test Production Configuration Locally:**

```powershell
# 1. Create appsettings.Production.json with production values
Copy-Item appsettings.json appsettings.Production.json

# 2. Edit appsettings.Production.json - add real M3 credentials
# (Or use environment variables instead of file)

# 3. Set Production environment
$env:ASPNETCORE_ENVIRONMENT = "Production"

# 4. Set secrets via environment variables (not User Secrets)
$env:M3__HOST = "150.3.2.100"
$env:M3__PORT = "6300"
$env:M3__USER = "MBUSER"
$env:M3__PASSWORD = "your-password"
$env:M3__DEFAULT_COMPANY = "300"
$env:ApiKeys__Primary = "your-api-key"

# 5. Run
dotnet run

# 6. Verify configuration is loaded from environment (not User Secrets)
# Check logs for: "Environment: Production"
```

**Verify Active Environment:**

```powershell
# Start the API
dotnet run

# Check console output for:
# [INFO] Environment: Development  (or Production)
# [INFO] Using appsettings.Development.json (or Production)
```

**Configuration Loading Order (by priority, highest first):**

1. **Command-line arguments** (highest priority)
2. **Environment variables** (e.g., `M3_HOST`, `ApiKeys__Primary`)
3. **User Secrets** (Development only, via `dotnet user-secrets`)
4. **appsettings.{Environment}.json** (e.g., `appsettings.Production.json`)
5. **appsettings.json** (base configuration, lowest priority)

**Example: Full Production Local Test**

```powershell
# Set all required environment variables
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:M3__HOST = "150.3.2.100"
$env:M3__PORT = "6300"
$env:M3__USER = "MBUSER"
$env:M3__PASSWORD = "your-password"
$env:M3__DEFAULT_COMPANY = "300"
$env:ApiKeys__Primary = "production-api-key"
$env:ApiKeys__Admin = "production-admin-key"
$env:ConnectionPool__MinSize = "5"
$env:ConnectionPool__MaxSize = "20"

# Run in production mode
dotnet run

# Test the API
$headers = @{ "X-API-Key" = $env:ApiKeys__Primary }
Invoke-RestMethod -Uri "http://localhost:5000/health" -Headers $headers
```

**Reset to Development:**

```powershell
# Clear production environment variables
Remove-Item Env:\ASPNETCORE_ENVIRONMENT
Remove-Item Env:\M3__HOST
Remove-Item Env:\M3__PORT
Remove-Item Env:\M3__USER
Remove-Item Env:\M3__PASSWORD
Remove-Item Env:\M3__DEFAULT_COMPANY
Remove-Item Env:\ApiKeys__Primary
Remove-Item Env:\ApiKeys__Admin

# Or set back to Development explicitly
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Run normally (uses User Secrets)
dotnet run
```

**Important Notes:**

⚠️ **User Secrets do NOT work in Production mode** - You must use environment variables or configuration files  
⚠️ **Do NOT commit `appsettings.Production.json` with real credentials** - It's in `.gitignore` for this reason  
⚠️ **In IIS/Production server**, environment is set via IIS App Pool configuration, not PowerShell variables

### Troubleshooting Development Issues

| Issue | Solution |
|-------|----------|
| **"M3:HOST is null"** | Run `dotnet user-secrets list` to verify secrets are set |
| **401 Unauthorized** | Check API key: `dotnet user-secrets list \| Select-String ApiKeys:Primary` |
| **Connection refused to M3** | Verify M3 server is reachable: `Test-NetConnection 150.3.2.100 -Port 6300` |
| **Build errors** | Run `dotnet clean` then `dotnet restore` and `dotnet build` |
| **Port already in use** | Change port in `appsettings.json` or `launchSettings.json` |
| **Logs not appearing** | Check `logs/` directory exists; verify Serilog config in `appsettings.json` |

### Next Steps

- **Add business logic**: Implement custom workflows in `Services/`
- **Add validation**: Extend `ItemValidationService` or create new validators
- **Configure resilience**: Adjust Polly policies in `Infrastructure/Resilience/PollyPolicies.cs`
- **Deploy to test environment**: See [docs/IIS_DEPLOYMENT_SRXWEBAPP1.md](docs/IIS_DEPLOYMENT_SRXWEBAPP1.md)

## Quick Start

1. **Configure M3 connection:**
```bash
cd C:\Projects\MOVEX\API-Integration\movex-rest-api
dotnet user-secrets set "M3:HOST" "150.3.2.100"
dotnet user-secrets set "M3:PORT" "6300"
dotnet user-secrets set "M3:USER" "<your-user>"
dotnet user-secrets set "M3:PASSWORD" "<your-password>"
dotnet user-secrets set "M3:DEFAULT_COMPANY" "300"
```

2. **Generate API key:**
```bash
.\scripts\generate-api-key-simple.ps1
```

3. **Run the server:**
```bash
dotnet run
```

Server starts at `http://localhost:5000`

## Configure (secrets-safe)

Avoid plaintext secrets in source. Use **User Secrets** for local dev and environment variables for CI/Prod.

### API Keys
Generate and store a strong API key in User Secrets:

```bash
# Quick: generates a 256-bit Base64 key and stores it under ApiKeys:Primary
cd c:/Projects/MOVEX/API-Integration/movex-rest-api
.\scripts\generate-api-key-simple.ps1

# Advanced: Base64 or Hex, configurable length
.\scripts\generate-api-key.ps1 -Format Hex -Verify
```

Then set an admin key (for key rotation and status endpoints):
```bash
dotnet user-secrets set "ApiKeys:Admin" "your-admin-key"
```

### M3 Credentials & Configuration

```bash
dotnet user-secrets init

# M3 Connection Settings
dotnet user-secrets set "M3:HOST" "150.3.2.100"
dotnet user-secrets set "M3:PORT" "6300"
dotnet user-secrets set "M3:USER" "<your-username>"
dotnet user-secrets set "M3:PASSWORD" "<your-password>"

# M3 Default Company (optional, defaults to 300)
dotnet user-secrets set "M3:DEFAULT_COMPANY" "300"
```

### CI/Prod (Environment Variables)

Provide all secrets via your platform's secret store (e.g., Key Vault/Secrets Manager) so they arrive as environment variables:

**M3 Configuration:**
- `M3:HOST` — M3 server hostname/IP
- `M3:PORT` — M3 MI server port  
- `M3:USER` — M3 username
- `M3:PASSWORD` — M3 password
- `M3:DEFAULT_COMPANY` — Default company number for transactions (optional, defaults to 300)

**Other Configuration Variables:**
- `ApiKeys:Primary` — Primary API key
- `ApiKeys:Admin` — Admin key (for rotation)
- `ConnectionPool:MinSize`, `ConnectionPool:MaxSize` — Connection pool sizing (optional, defaults: 2 min, 10 max)
- `ConnectionPool:IdleTimeoutSeconds` — Idle connection timeout (optional, default: 300s)
- `AllowedOrigins` — Configure CORS; if none are set, cross-origin requests are blocked by default

**Example: Setting Environment Variables for Production**

```powershell
# PowerShell
$env:M3__HOST = "150.3.2.100"
$env:M3__PORT = "6300"
$env:M3__USER = "MBUSER"
$env:M3__PASSWORD = "your-password"
$env:M3__DEFAULT_COMPANY = "300"  # Can be overridden per environment
$env:ApiKeys__Primary = "your-api-key"
```

```bash
# Linux/macOS (bash)
export M3__HOST="150.3.2.100"
export M3__PORT="6300"
export M3__USER="MBUSER"
export M3__PASSWORD="your-password"
export M3__DEFAULT_COMPANY="300"
export ApiKeys__Primary="your-api-key"
```

⚠️ **Configuration Format:** Use `__` (double underscore) in environment variables to represent nested JSON sections (e.g., `M3__DEFAULT_COMPANY` maps to `M3:DEFAULT_COMPANY` in `appsettings.json`)

`appsettings.json` now contains placeholders only; real values must come from secrets or env vars.

### Logging Configuration

The API uses **Serilog** for structured logging with automatic file rotation:

**Log Files:**
- Location: `logs/` directory (created automatically)
- Format: `movex-api-YYYY-MM-DD.txt` (one file per day)
- Retention: 30 days (older logs auto-deleted)
- Size limit: 100 MB per file (triggers rotation within daily file)

**Log Levels** (configured in `appsettings.json`):
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

Available levels: `Debug`, `Information`, `Warning`, `Error`, `Fatal`

**Environment Override:**
Set log level via environment variable:
```bash
# Set to Debug for detailed troubleshooting
export Logging__LogLevel__Default=Debug

# Set to Warning for minimal logging in production
export Logging__LogLevel__Default=Warning
```

**Log Output:**
- **Console**: Real-time logs during development
- **File**: Persistent logs in `logs/` directory for production auditing

Example log entry:
```
[2026-01-12 14:30:45.123 +00:00] [INF] Executing transaction: MMS200MI/GetItmBasic
[2026-01-12 14:30:45.456 +00:00] [INF] Response parsed: ITNO=Item001, STAT=20, ITTY=030
[2026-01-12 14:30:46.789 +00:00] [ERR] Connection timeout: M3 server unreachable
```

### Connection Pooling

The API uses a thread-safe connection pool to reuse M3 socket connections and reduce overhead:
- **Min pool size**: 2 (default) — Pre-created connections on startup
- **Max pool size**: 10 (default) — Maximum concurrent connections
- **Idle timeout**: 300s (5 min, default) — Stale connections are cleaned up
- **Acquire timeout**: 10s (default) — Max wait time to get a connection from the pool

Configure via `appsettings.json` or environment variables (`ConnectionPool:MinSize`, etc.).

### Override MI program/library

- Default: `MVXLIB/MMS200MI`
- To use another library/program (e.g., `MVXCOBJ/MMS200MI`), send `"APIPROGRAM": "MVXCOBJ/MMS200MI"` in the POST body. The service extracts library/program and logs in accordingly.

## Build and Run

```bash
dotnet restore
dotnet build
dotnet run
```

Server listens on `http://localhost:5000` by default (see `launchSettings.json` or `appsettings.json`).

## API Endpoints

### Authentication
All endpoints (except `/health`) require:
- **`X-API-Key`** header with the primary API key, or
- **`X-Admin-Key`** header with the admin key (for admin-only routes)

### Program & Transaction Discovery
- `GET /api/programs` — List all available M3 programs
- `GET /api/programs/{program}` — Get program metadata and transactions
- `GET /api/{program}/{transaction}/info` — Get transaction field metadata

### Transaction Execution
- `POST /api/{program}/{transaction}` — Execute M3 transaction (default)
- `GET /api/{program}/{transaction}` — Execute transaction configured as GET (query string parameters)

Transactions can specify the HTTP method in their JSON config. If a transaction is configured as `GET`, the POST endpoint will return a 400 explaining to use GET.

### Security/Admin Endpoints
- `GET /api/security/api-key/status` — Check if API key is configured (no key value revealed)
- `POST /api/security/api-key/rotate` — *(admin-only, Development)* Rotate primary API key

### MMS175MI - Movement (Change Location)
- `POST /api/mms175mi/update` — Transfer item between warehouse locations
  - **Parameters**: `WHLO` (warehouse), `ITNO` (item), `WHSL` (from location), `TWSL` (to location), `TRQT` (quantity)
  - **Optional**: `BANO` (batch/lot), `CAMU` (container)
  - **Example**: Transfer 10 units of item DC210055 from location D07AA5 to D08BB2
  - Full documentation: [docs/MMS175MI.md](docs/MMS175MI.md)

### Business Workflows
- `POST /api/workflows/StockManagement` — MES Stock Management System (SMS) workflow
  - Validates item status (STAT=20/50) and type (ITTY=030)
  - Checks warehouse balance (on-hand >= requested qty)
  - Updates stock via MMS310MI with server-enforced company 300
  - Returns 422 if validation fails or insufficient stock
- `POST /api/workflows/GetStandardCost` — MES Check Standard Cost workflow
  - Validates item status (STAT=20/50) and type (ITTY=030)
  - Retrieves unit cost, facility name, and item details from MMS200MI.GetItmFac (consolidated workflow)
  - Returns 422 if validation fails or facility does not exist
- `POST /api/workflows/AdjustStockWithValidation` — Simple stock adjustment (legacy)
  - Validates item status (STAT=20/50) and type (ITTY=030)
  - Adjusts stock via MMS310MI with server-enforced company 300
  - Returns 422 if validation fails, 200 on success

**Error Codes:**
- `001` - Item Does Not Exist
- `002` - Item is not active/released (STAT must be 20 or 50)
- `003` - Item is not a raw material (ITTY must be 030)
- `004` - Insufficient available stock in warehouse
- `011` - Facility Does Not Exist

### Pool Monitoring
- `GET /api/transactions/pool-stats` — Connection pool statistics (requires API key)

### Health
- `GET /health` — Health check (no authentication required)

## Resilience & Reliability

### Circuit Breaker Pattern (Polly)

The API includes **Polly-based resilience** for M3 transaction execution:

**Retry Strategy:**
- **Max retries**: 3 attempts
- **Backoff**: Exponential (200ms → 400ms → 800ms)
- **Triggers**: `HttpRequestException`, `TimeoutException`, `OperationCanceledException`

**Circuit Breaker:**
- **Threshold**: 3 failures before opening
- **Open duration**: 30 seconds (halts requests to M3)
- **States**: Closed (normal) → Open (failing) → Half-Open (testing) → Closed (recovered)

**Benefits:**
- Prevents cascading failures when M3 is down
- Automatic recovery with exponential backoff
- Protects connection pool during transient issues
- Reduces load on failing M3 servers

**When circuit breaker opens:**
- API returns 503 Service Unavailable immediately (no retry)
- After 30s, circuit enters half-open state
- Next transaction triggers a test call to M3
- If successful, circuit closes and traffic resumes
- If fails, circuit reopens for another 30s

**Logging:**
Circuit breaker state changes are logged at WARN level:
```
Retry #1 after 200ms. Reason: Connection timeout
Retry #2 after 400ms. Reason: Connection timeout
Retry #3 after 800ms. Reason: Connection timeout
Circuit breaker opened for 30 seconds
Circuit breaker half-open - testing M3 connection
Circuit breaker reset - M3 connection restored
```

### Examples

**List all programs:**
```bash
curl -X GET http://localhost:5000/api/programs \
  -H "X-API-Key: your-api-key"
```
MMS200MI/GetItmBasic \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"CONO":"100","ITNO":"Item123"}'
```

**Execute with parsed response:**
```bash
# If responseFields are configured in the transaction JSON, 
# the response will be automatically parsed into typed fields
curl -X POST http://localhost:5000/api/MMS200MI/GetItmBasic \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"CONO":"100","ITNO":"ABC123"}'

# Response:
{
  "success": true,
  "program": "MMS200MI",
  "transaction": "GetItmBasic",
  "data": {
    "CONO": 100,
    "ITNO": "ABC123",
    "ITDS": "Item Description",
    "UNMS": "EA"
  },
  "timestamp": "2026-01-05T17:30:00Z"
}
  -H "X-API-Key: your-api-key"
```

**Get transaction info:**
```bash
curl -X GET http://localhost:5000/api/MMS200MI/GetItmBasic/info \
  -H "X-API-Key: your-api-key"
```

**Stock adjustment with validation (workflow):**
```bash
curl -X POST http://localhost:5000/api/workflows/AdjustStockWithValidation \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{
    "ITNO": "ITEM001",
    "WHLO": "D01",
    "WHSL": "L01",
    "STQI": "100",
    "BREM": "Initial inventory count"
  }'

# Response on success:
{
  "success": true,
  "program": "MMS310MI",
  "transaction": "Update",
  "validation": {
    "stat": 20,
    "itty": "030"
  },
  "data": {
    "message": "Update executed"
  },
  "timestamp": "2026-01-12T10:30:00Z"
}

# Response on validation failure (item not active):
{
  "success": false,
  "error": "Item status 30 is not allowed (must be 20)",
  "stat": 30,
  "itty": "030"
}
```

## MES Business Workflows

### Stock Management System (SMS) - Requirement 1

The `/api/workflows/StockManagement` endpoint implements the full MES stock management flow with validation and balance checking:

**Flow:**
1. Client sends: `ITNO` (item number), `WHLO` (warehouse), `WHSL` (location), `STQI` (new quantity)
2. Server validates item via `MMS200MI.GetItmBasic`:
   - Item status (`STAT`) must be 20 (Active) or 50 (Released)
   - Item type (`ITTY`) must be "030" (raw material)
3. Server checks warehouse balance via `MMS200MI.GetItmWhsBal`:
   - On-hand balance (`STQT`) must be >= new quantity
4. Server updates stock via `MMS310MI.Update`:
   - Company enforced to 300
   - Remark automatically set to "MES SMS"
5. Returns success or structured error code

**Request Body:**
```json
{
  "ITNO": "DC210055",  // Required: Item number
  "WHLO": "D01",       // Required: Warehouse
  "WHSL": "D07AA5",    // Required: Location
  "STQI": "150"        // Required: New stock quantity
}
```

**Response Codes:**
- **200**: Success - item validated, balance checked, and stock updated
- **400**: Missing required fields
- **422**: Validation failed (see errorCode: 001-004)
- **500**: Server error

**Success Response:**
```json
{
  "success": true,
  "program": "MMS310MI",
  "transaction": "Update",
  "validation": { "stat": 20, "itty": "030" },
  "balanceCheck": { "onHandBalance": 200, "newQuantity": 150 },
  "data": { "message": "Stock updated" },
  "timestamp": "2026-01-12T10:30:00Z"
}
```

**Error Response (Validation Failed):**
```json
{
  "success": false,
  "errorCode": "002",
  "error": "Item is not active/released"
}
```

### Check Standard Cost - Requirement 2

The `/api/workflows/GetStandardCost` endpoint validates an item and retrieves its unit cost and facility information using a consolidated MMS200MI call:

**Flow:**
1. Client sends: `ITNO` (item number), `FACI` (facility code)
2. Server validates item via `MMS200MI.GetItmBasic`:
   - Item status (`STAT`) must be 20 (Active) or 50 (Released)
   - Item type (`ITTY`) must be "030" (raw material)
3. Server retrieves cost and facility info via `MMS200MI.GetItmFac`:
   - Returns unit cost price (`UCOS`)
   - Returns facility name (`FACN`)
   - Returns item description (`ITDS`)
4. Returns success with consolidated information or structured error code

**Workflow Optimization:**
The workflow was redesigned to use `MMS200MI.GetItmFac` instead of `MMS003.Get` for the following benefits:
- **Single M3 Call**: Consolidated data retrieval reduces network overhead
- **Rich Response**: Includes facility name and item description in addition to unit cost
- **Better Performance**: One transaction instead of two network round-trips
- **Consistent Data Source**: All item information from unified MMS200MI program

**Request Body:**
```json
{
  "ITNO": "DC210055",  // Required: Item number
  "FACI": "001"        // Required: Facility code
}
```

**Response Example:**
```json
{
  "success": true,
  "program": "MMS200MI",
  "transaction": "GetItmFac",
  "validation": {
    "stat": 20,
    "itty": "030"
  },
  "data": {
    "unitCostPrice": 125.50,
    "facility": "001",
    "facilityName": "Main Warehouse",
    "itemNumber": "DC210055",
    "itemDescription": "Raw Material Component"
  },
  "timestamp": "2025-01-13T10:30:00Z"
}
```

**Response Codes:**
- **200**: Success - item validated and cost + facility info retrieved
- **400**: Missing required fields
- **422**: Validation failed or facility does not exist (see errorCode)
- **500**: Server error

**Success Response:**
```json
{
  "success": true,
  "program": "MMS200MI",
  "transaction": "GetItmFac",
  "validation": { "stat": 20, "itty": "030" },
  "data": {
    "unitCostPrice": 12.50,
    "facility": "001",
    "facilityName": "Main Warehouse",
    "itemNumber": "DC210055",
    "itemDescription": "Raw Material Component"
  },
  "timestamp": "2026-01-12T10:30:00Z"
}
```

## Stock Adjustment Workflow (Legacy)

The `/api/workflows/AdjustStockWithValidation` endpoint implements server-side validated stock adjustment without balance checking:

**Flow:**
1. Client sends: `ITNO`, `WHLO`, `WHSL`, `STQI`, (optional `STAG`, `STDI`, `BREM`)
2. Server validates:
   - Item status (`STAT`) must be 20 (Active) or 50 (Released)
   - Item type (`ITTY`) must equal "030" (Stock Item)
   - STAG (if provided) must be 1, 2, or 3
3. If validation passes: Server calls `MMS310MI.Update` to adjust stock
4. Returns success or detailed error

**Request Body:**
```json
{
  "ITNO": "Item001",           // Required: Item number
  "WHLO": "D01",               // Required: Warehouse
  "WHSL": "L01",               // Required: Location
  "STQI": "150",               // Required: Stock quantity to set
  "STAG": "2",                 // Optional: Item Status (1=Under Inspection, 2=Approved, 3=Rejected, defaults to 2)
  "STDI": "20260113",          // Optional: Physical Inventory Date (YYYYMMDD, defaults to current date)
  "BREM": "Q1 inventory"       // Optional: Remark/reason
}
```

**Response Codes:**
- **200**: Item validated and stock adjusted successfully
- **400**: Missing required fields (ITNO, WHLO, WHSL, STQI) or invalid STAG value
- **422**: Validation failed (item status/type not allowed for adjustment)
- **502**: M3 communication error during validation
- **500**: Server error

## Config-Driven Architecture
The API is **fully config-driven** - add new M3 programs and transactions via JSON files in the `transactions/` directory:

**Example: `transactions/MMS200MI.json`**
```json
{
  "program": "MMS200MI",
  "library": "MVXCOBJ",
  "description": "Item Master Management",
  "transactions": [
    {
      "name": "GetItmBasic",
      "description": "Get Item Basic Information",
      "fields": [
        {
          "name": "CONO",
          "position": 1,
          "length": 3,
          "type": "numeric",
          "required": true,
          "description": "Company Number"
        },
        {
          "name": "ITNO",
          "position": 2,
          "length": 40,
          "type": "text",
          "required": true,
          "description": "Item Number"
        }
      ],
      "responseFields": [
        {
          "name": "CONO",
          "position": 1,
          "length": 3,
          "type": "numeric",
          "description": "Company Number"
        },
        {
          "name": "ITNO",
          "position": 2,
          "length": 40,
          "type": "text",
          "required": true,
          "description": "Item Number"
        },
        {
          "name": "ITDS",
          "position": 3,
          "length": 30,
          "type": "text",
          "description": "Item Description"
        }
      ]
    }
  ]
}
```

**Hot Reload:** Changes to config files are automatically detected and reloaded (60s cleanup interval).

**Key components:**
- `TransactionConfigurationService` loads JSON configs from `transactions/` with a FileSystemWatcher.
- `TransactionStringBuilder` builds transaction strings from config and validates fields.
- `M3ResponseParser` parses raw responses into typed objects (numeric→decimal, integer→int, date→ISO8601, boolean→bool).
- `TransactionService` orchestrates: config lookup → string building → connection → execution → parsing.
- `TransactionController` exposes REST endpoints with dynamic routing (`/api/{program}/{transaction}`).
### Response Parsing & Type Conversion
When `responseFields` are configured in the transaction JSON, responses are automatically parsed:

**Supported Types:**
- **`numeric`** → `decimal` (e.g., "00123" → 123.0)
- **`integer`** → `int` (e.g., "00042" → 42)
- **`decimal`** → `decimal`
- **`date`** → ISO 8601 string (`yyyy-MM-dd`)
- **`boolean`** → `bool` (1/Y/T → true, 0/N/F → false)
- **`text`** → `string` (trimmed)

### Config Validation
All transaction configs are validated on load using `ConfigurationValidator`:
- Required fields: `program`, `library`, `description`, `transactions`
- Field validation: `name`, `position`, `length`, `type`, `required`
- Type validation: `numeric`, `text`, `date`, `boolean`, `integer`, `decimal`
- Position uniqueness within transaction
- Invalid configs are rejected with detailed error messages

Example parsed response:
```json
{
  "CONO": 100,
  "ITNO": "ABC123",
  "STQT": 250,
  "UNPR": 12.50,
  "CRDT": "2026-01-05",
  "STTS": true
}
```

### Clean Architecture & Separation of Concerns

```
Movex.API/
├── Core/
│   ├── Constants/
│   │   └── ValidationPatterns.cs        # Regex patterns for field validation
│   ├── Interfaces/                      # Service contracts
│   │   ├── ITransactionService.cs       # Main orchestration service
│   │   ├── ITransactionStringBuilder.cs # Transaction string builder
│   │   ├── IFieldValidator.cs           # Field validation
│   │   ├── IResponseParser.cs           # Response parsing
│   │   └── ITransactionConfigurationService.cs
│   └── Models/
│       └── ConfigurationModels.cs       # FieldConfig, TransactionDefinition, etc.
├── Infrastructure/
│   ├── Configuration/
│   │   ├── TransactionConfigurationService.cs  # Config loading & hot-reload
│   │   └── ConfigurationValidator.cs           # JSON schema validation
│   ├── ConnectionPool/
│   │   └── MvxConnectionPool.cs         # Thread-safe connection pooling
│   ├── M3Protocol/
│   │   └── MvxSockWrapper.cs            # Socket protocol (ISO-8859-1)
│   └── TransactionBuilding/
│       └── TransactionStringBuilder.cs  # Build transaction strings from config
├── Services/
│   ├── TransactionService.cs            # Orchestration (100 lines!)
│   ├── M3ResponseParser.cs              # Parse & type convert responses
│   └── Validation/
│       └── FieldValidator.cs            # Field validation logic
└── Controllers/
    └── TransactionController.cs         # REST endpoints (generic + workflows)
```

**Key Benefits:**
- **Single Responsibility:** Each class has one clear purpose
- **Dependency Injection:** All dependencies via interfaces (fully testable)
- **No Legacy Code:** 0% hardcoded transactions
- **Extensible:** Add new programs without touching code

### Async/Await Architecture (Scalability)
The API uses **fully async I/O** throughout the stack for non-blocking request handling:

- **`MvxSockWrapper`**: Async socket operations via `ConnectAsync()`, `LoginAsync()`, `ExecuteAsync()`, and `SendReceiveAsync()` using `Socket.SendAsync()` / `Socket.ReceiveAsync()` with built-in timeout tokens.
- **`MvxConnectionPool`**: Async connection acquisition via `AcquireConnectionAsync()` with `WaitAsync()` on semaphores for non-blocking pool access.
- **`TransactionService`**: Service method `ExecuteTransactionAsync()` orchestrates config lookup, string building, connection acquisition, execution, and response parsing
  -H "X-API-Key: your-api-key" \
  -d '{"CONO":"100","ITNO":"Item123","APIPROGRAM":"MVXCOBJ/MMS200MI"}'
```

**Check API key status:**
```bash
curl -X GET http://localhost:5000/api/security/api-key/status \
  -H "X-API-Key: your-api-key"
```

**Rotate API key (admin-only):**
```bash
curl -X POST "http://localhost:5000/api/security/api-key/rotate?format=Base64&bytes=32" \
  -H "X-Admin-Key: your-admin-key"
```

## How it works

### Async/Await Architecture (Scalability)
The API uses **fully async I/O** throughout the stack for non-blocking request handling:
No programs available**: Check that JSON config files exist in `transactions/` directory
- **Config validation errors**: Review error messages and ensure JSON matches schema (see `transactions/schema.json`)
- **Transaction not found**: Verify transaction name matches config (case-insensitive)
- **Login fails**: Verify username/password and that the account is authorized for the specified library/program
- **Connection fails**: Check `M3_HOST`/`M3_PORT` reachability (firewall/VPN)

## Adding New M3 Programs

1. Create a new JSON file in `transactions/` directory (e.g., `CRS610MI.json`)
2. Follow the schema structure (see `transactions/schema.json`)
3. Define program metadata, transactions, fields, and response fields
4. The config is automatically loaded (hot-reload within 60s)
5. Test with `GET /api/programs/{program}`

No code changes required!

## Project Structure

```
movex-rest-api/
├── Core/
│   ├── Constants/ValidationPatterns.cs
│   ├── Interfaces/ (6 interfaces)
│   └── Models/ConfigurationModels.cs
├── Infrastructure/
│   ├── Configuration/
│   │   ├── TransactionConfigurationService.cs
│   │   └── ConfigurationValidator.cs
│   ├── ConnectionPool/MvxConnectionPool.cs
│   ├── M3Protocol/MvxSockWrapper.cs
│   └── TransactionBuilding/TransactionStringBuilder.cs
├── Services/
│   ├── TransactionService.cs
│   ├── M3ResponseParser.cs
│   └── Validation/FieldValidator.cs
├── Controllers/MMS200MIController.cs
├── transactions/
│   ├── schema.json
│   └── MMS200MI.json
├── Program.cs
├── Movex.API.csproj
- `MvxSockWrapper` implements the MI socket protocol in C# (ISO-8859-1 encoding). No native DLLs.
- `MMS200MIService` builds the transaction string, acquires a pooled connection, and executes with optional `APIPROGRAM` override.
- `MMS200MIController` exposes REST endpoints.
- Detailed console logging is enabled for connect/login/execute paths, pool operations, and exceptions, while sensitive payloads are redacted (only lengths logged).

### CORS configuration
- Set explicit origins via `AllowedOrigins` (array). Example:
  - `https://localhost:3000`
  - `https://example.com`
- If `AllowedOrigins` is empty or missing, cross-origin requests are blocked.

### Security Defaults (current)
- **API keys required** for all routes except `/health`; admin key for rotation.
- **CORS**: only explicitly configured `AllowedOrigins` are permitted; if none are configured, cross-origin requests are blocked.
- **Input validation**: rejects unknown fields, enforces `APIPROGRAM` as `LIB/PROGRAM`, numeric fields digits-only, text fields limited to `[A-Za-z0-9 _-./:]`, required fields enforced.

## GET Transactions and List Responses (New)

Transactions can now declare the HTTP method and whether the response returns multiple records through JSON config:

```json
{
  "name": "LstItmFac",
  "description": "List Item Facilities",
  "httpMethod": "GET",
  "returnsList": true,
  "fields": [
    { "name": "CONO", "position": 1, "length": 3, "type": "numeric", "required": true },
    { "name": "FACI", "position": 2, "length": 3, "type": "text", "required": false },
    { "name": "ITNO", "position": 3, "length": 15, "type": "text", "required": false }
  ],
  "responseFields": [
    { "name": "CONO", "position": 1, "length": 3, "type": "numeric" },
    { "name": "FACI", "position": 2, "length": 3, "type": "text" },
    { "name": "ITNO", "position": 3, "length": 15, "type": "text" }
  ]
}
```

### How to call a GET-configured transaction

Use query parameters:

```bash
curl -X GET "http://localhost:5000/api/MMS200MI/LstItmFac?CONO=100&FACI=001&ITNO=6100001JN" \
  -H "X-API-Key: your-api-key"
```

Response shape for list transactions:

```json
{
  "success": true,
  "program": "MMS200MI",
  "transaction": "LstItmFac",
  "data": {
    "records": [
      { "CONO": 100, "FACI": "001", "ITNO": "6100001JN" },
      { "CONO": 100, "FACI": "001", "ITNO": "6100002JN" }
    ],
    "count": 2
  },
  "timestamp": "2026-01-07T12:00:00Z"
}
```

### Config keys
- `httpMethod`: "GET" or "POST" (default POST). Controls which endpoint is allowed for the transaction.
- `returnsList`: `true` if the MI returns multiple records; the API will parse into `data.records` with `count`.
- **Logging**: redacts payloads (logs lengths instead of contents) to reduce sensitive data exposure.

### Async/Performance Notes
- All request handlers are fully async (`async Task<IActionResult>`) and non-blocking, enabling efficient handling of concurrent requests.
- Socket operations use cancellation tokens with configurable timeouts (SEND: 2s, RECEIVE: 30s).
- Connection pool uses `SemaphoreSlim.WaitAsync()` for non-blocking slot acquisition.
- Cleanup timer runs every 60 seconds to remove stale idle connections (never below min pool size).

## Troubleshooting

### Log Files Location
- **Linux/Mac**: `./logs/` (relative to app working directory)
- **Windows**: `C:\Projects\MOVEX\API-Integration\movex-rest-api\logs\` (or wherever you run `dotnet run`)
- **Docker**: Mount volume at `/app/logs` to persist logs outside container

### Common Issues & Solutions

**Program not found**
- Ensure the library/program exists on the M3 host. If it is `MVXCOBJ/MMS200MI`, include `APIPROGRAM` in your request body.
- Check logs for config loading errors: `[ERR] Failed to load configuration for program MMS200MI`

**Login fails**
- Verify username/password and that the account is authorized for the specified library/program.
- Check logs: `[ERR] Login failed for user {user} on {host}:{port}`
- Enable Debug logging to see full socket protocol details

**Connection fails**
- Check `M3_HOST`/`M3_PORT` reachability (firewall/VPN)
- Verify network connectivity: `ping {M3_HOST}`
- Check logs: `[ERR] Unable to connect to {host}:{port} - Connection refused`

**Validation errors (422 Unprocessable Entity)**
- Check error code in response (001-004, 011)
- Review logs for validation details: `[WRN] Validation failed for item {ITNO}: errorCode={code}`

**Circuit breaker open (503 Service Unavailable)**
- M3 server is unreachable or experiencing issues
- Check M3 server logs and connectivity
- API will retry automatically after 30 seconds
- Monitor logs: `[WRN] Circuit breaker opened for 30 seconds`

**File Logging Issues**
- **Logs not being created**: Check file permissions on `logs/` directory
- **Large log files**: Set `Logging:LogLevel:Default` to `Warning` to reduce volume
- **Old logs not deleted**: Ensure app has permission to delete old log files (30-day retention)
- **Debug logs missing**: Set `Logging:LogLevel:Default` to `Debug` in `appsettings.json`

### Viewing Logs

**Real-time logs (console)**:
```bash
dotnet run
# Logs stream to console in real-time
```

**File-based logs (persistent)**:
```bash
# Linux/Mac
tail -f logs/movex-api-$(date +%Y-%m-%d).txt

# Windows PowerShell
Get-Content -Path logs\movex-api-2026-01-12.txt -Tail 50 -Wait
```

**Search for errors in logs**:
```bash
# Linux/Mac
grep "\[ERR\]" logs/movex-api-*.txt

# Windows PowerShell
Select-String -Path logs/*.txt -Pattern "\[ERR\]" | Select-Object -Last 20
```

**Log Levels at a Glance**:
- `[DBG]` - Debug: Detailed diagnostics (socket operations, field values)
- `[INF]` - Info: Normal operation flow (transaction execution, validation pass)
- `[WRN]` - Warning: Recoverable issues (circuit breaker state, retry attempts)
- `[ERR]` - Error: Failures that need attention (connection lost, validation failed, M3 error)
