# Movex REST API - .NET (Config-Driven Architecture)

ASP.NET Core REST API for M3 MOVEX using a pure managed socket client (no mvxsock.dll required). 
**Config-driven architecture** supports multiple M3 programs through JSON configuration files.

Tested on .NET 8.

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

## Quick Start

1. **Configure M3 connection:**
```bash
cd C:\Projects\MOVEX\API-Integration\movex-rest-api
dotnet user-secrets set "M3_HOST" "150.3.2.100"
dotnet user-secrets set "M3_PORT" "6000"
dotnet user-secrets set "M3_USER" "<your-user>"
dotnet user-secrets set "M3_PASSWORD" "<your-password>"
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

### M3 Credentials
```bash
dotnet user-secrets init
dotnet user-secrets set "M3_HOST" "150.3.2.100"
dotnet user-secrets set "M3_PORT" "6000"
dotnet user-secrets set "M3_USER" "<your-user>"
dotnet user-secrets set "M3_PASSWORD" "<your-password>"
```

### CI/Prod (Environment Variables)
Provide all secrets via your platform's secret store (e.g., Key Vault/Secrets Manager) so they arrive as environment variables:
- `ApiKeys:Primary` — Primary API key
- `ApiKeys:Admin` — Admin key (for rotation)
- `M3_HOST`, `M3_PORT`, `M3_USER`, `M3_PASSWORD` — M3 connection
- `ConnectionPool:MinSize`, `ConnectionPool:MaxSize` — Connection pool sizing (optional, defaults: 2 min, 10 max)
- `ConnectionPool:IdleTimeoutSeconds` — Idle connection timeout (optional, default: 300s)
- `AllowedOrigins` — Configure CORS; if none are set, cross-origin requests are blocked by default

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

### Business Workflows
- `POST /api/workflows/StockManagement` — MES Stock Management System (SMS) workflow
  - Validates item status (STAT=20/50) and type (ITTY=030)
  - Checks warehouse balance (on-hand >= requested qty)
  - Updates stock via MMS310MI with server-enforced company 300
  - Returns 422 if validation fails or insufficient stock
- `POST /api/workflows/GetStandardCost` — MES Check Standard Cost workflow
  - Validates item status (STAT=20/50) and type (ITTY=030)
  - Retrieves unit cost price from MMS003
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
- `GET /api/pool/stats` — Connection pool statistics (requires API key)

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

The `/api/workflows/GetStandardCost` endpoint validates an item and retrieves its unit cost:

**Flow:**
1. Client sends: `ITNO` (item number), `FACI` (facility code)
2. Server validates item via `MMS200MI.GetItmBasic`:
   - Item status (`STAT`) must be 20 (Active) or 50 (Released)
   - Item type (`ITTY`) must be "030" (raw material)
3. Server retrieves cost via `MMS003.Get`:
   - Returns unit cost price (`UCOS`)
4. Returns success with cost or structured error code

**Request Body:**
```json
{
  "ITNO": "DC210055",  // Required: Item number
  "FACI": "001"        // Required: Facility code
}
```

**Response Codes:**
- **200**: Success - item validated and cost retrieved
- **400**: Missing required fields
- **422**: Validation failed or facility does not exist (see errorCode)
- **500**: Server error

**Success Response:**
```json
{
  "success": true,
  "program": "MMS003",
  "transaction": "Get",
  "validation": { "stat": 20, "itty": "030" },
  "data": {
    "unitCostPrice": 12.50,
    "facility": "001",
    "itemNumber": "DC210055"
  },
  "timestamp": "2026-01-12T10:30:00Z"
}
```

## Stock Adjustment Workflow (Legacy)

The `/api/workflows/AdjustStockWithValidation` endpoint implements server-side validated stock adjustment without balance checking:

**Flow:**
1. Client sends: `ITNO`, `WHLO`, `WHSL`, `STQI`, (optional `BREM`)
2. Server calls `MMS200MI.GetItmBasic` with `CONO=300` to fetch item
3. Server validates:
   - Item status (`STAT`) must be 20 (Active) or 50 (Released)
   - Item type (`ITTY`) must equal "030" (Stock Item)
4. If validation passes: Server calls `MMS310MI.Update` with `CONO=300` (enforced) to adjust stock
5. Returns success or detailed error

**Request Body:**
```json
{
  "ITNO": "Item001",      // Required: Item number
  "WHLO": "D01",          // Required: Warehouse
  "WHSL": "L01",          // Required: Location
  "STQI": "150",          // Required: Stock quantity to set
  "BREM": "Q1 inventory"  // Optional: Remark/reason
}
```

**Response Codes:**
- **200**: Item validated and stock adjusted successfully
- **400**: Missing required fields (ITNO, WHLO, WHSL, STQI)
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
   TransactionConfigurationService` loads JSON configs from `transactions/` directory with hot-reload via FileSystemWatcher.
- `TransactionStringBuilder` builds transaction strings from config and validates fields.
- `M3ResponseParser` parses raw responses into typed objects (numeric→decimal, integer→int, date→ISO8601, boolean→bool).
- `TransactionService` orchestrates: config lookup → string building → connection → execution → parsing.
- `MMS200MIController` exposes REST endpoints with dynamic routing (`/api/{program}/{transaction}`).
- Detailed logging via `ILogger<T>` for all operations; sensitive payloads are redacted (only lengths logged).

### Response Parsing & Type Conversion
When `responseFields` are configured in the transaction JSON, responses are automatically parsed:

**Supported Types:**
- **`numeric`** → `decimal` (e.g., "00123" → 123.0)
- **`integer`** → `int` (e.g., "00042" → 42)
- **`decimal`** → `decim
  - Rejects unknown fields
  - Numeric fields: digits only (`[0-9]*`)
  - Text fields: alphanumeric + limited symbols (`[A-Za-z0-9 _\-\./:]*`)
  - Required fields enforced
  - Field length validation
- **Logging**: uses `ILogger<T>` with redacted payloads (logs lengths instead of contents) to reduce sensitive data exposure.

### Config Validation
All transaction configs are validated on load using `ConfigurationValidator`:
- Required fields: `program`, `library`, `description`, `transactions`
- Field validation: `name`, `position`, `length`, `type`, `required`
- Type validation: `numeric`, `text`, `date`, `boolean`, `integer`, `decimal`
- Position uniqueness within transaction
- Invalid configs are rejected with detailed error messages
- **`boolean`** → `bool` (1/Y/T → true, 0/N/F → false)
- **`text`** → `string` (trimmed)

Example response:
```json
{
  "CONO": 100,           // numeric → decimal
  "ITNO": "ABC123",      // text → string (trimmed)
  "STQT": 250,           // integer → int
  "UNPR": 12.50,         // decimal → decimal
  "CRDT": "2026-01-05",  // date → ISO 8601
  "STTS": true           // boolean → bool
}
```
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
