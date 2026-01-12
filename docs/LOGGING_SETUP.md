# Logging Setup Complete

## Summary of Changes

### 1. NuGet Package Additions
Added Serilog ecosystem to [Movex.API.csproj](Movex.API.csproj):
- **Serilog** (v3.1.1) - Structured logging framework
- **Serilog.AspNetCore** (v8.0.1) - ASP.NET Core integration
- **Serilog.Sinks.File** (v5.0.0) - File output sink with rotation

### 2. Program.cs Configuration
Updated [Program.cs](Program.cs) to:
- Import Serilog namespaces (`using Serilog;`)
- Create `logs/` directory automatically if it doesn't exist
- Configure Serilog with `builder.Host.UseSerilog()` before building the app
- Support configurable log levels via `appsettings.json` or environment variables
- Set up dual output: **Console** (development) + **File** (production audit trail)

**File Logging Configuration:**
```csharp
.WriteTo.File(
    path: Path.Combine(logsDirectory, "movex-api-.txt"),
    rollingInterval: RollingInterval.Day,        // New file per day
    retainedFileCountLimit: 30,                  // Keep 30 days of logs
    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
    fileSizeLimitBytes: 104857600)               // 100 MB per file
```

### 3. Log File Details
- **Location**: `logs/` directory (auto-created in app directory)
- **Naming**: `movex-api-YYYY-MM-DD.txt` (one file per day)
- **Retention**: 30 days of logs (older files auto-deleted)
- **Size Limit**: 100 MB per file
- **Format**: `[timestamp] [level] message`
- **Example**: `[2026-01-12 14:30:45.123 +00:00] [INF] Executing transaction: MMS200MI/GetItmBasic`

### 4. README Documentation
Updated [README.md](README.md) with:
- **Logging Configuration** section (lines 93-118) describing:
  - File location and naming scheme
  - Retention policy (30 days)
  - Log level configuration options
  - Environment variable overrides
  - Console vs. file output
  
- **Enhanced Troubleshooting** section (lines 746-820) with:
  - Log file location for Linux/Mac/Windows/Docker
  - 6 common issues with solutions and example log messages
  - How to view logs (real-time and file-based)
  - Log level legend (DBG, INF, WRN, ERR)
  - Search patterns for finding errors in log files

### 5. Configuration (appsettings.json)
Updated [appsettings.json](appsettings.json):
- Log level defaults: `Information` for app, `Warning` for ASP.NET framework
- Supports override via environment variable: `Logging__LogLevel__Default=Debug`
- All sensitive values remain as placeholders (use secrets in production)

## How Logging Works Now

### Development
```bash
cd C:\Projects\MOVEX\API-Integration\dotnet-poc
dotnet run

# Output to console in real-time:
[2026-01-12 14:30:45.123 +00:00] [INF] Server starting...
[2026-01-12 14:30:46.456 +00:00] [INF] Executing transaction: MMS200MI/GetItmBasic
```

### Production
```bash
# Logs persist to files automatically
# Check logs/movex-api-2026-01-12.txt

# Also can be queried for errors:
# Select-String -Path logs/*.txt -Pattern "\[ERR\]"
```

### Debug Mode
```bash
# Override to Debug level for detailed troubleshooting
export Logging__LogLevel__Default=Debug
dotnet run
```

## File Rotation Example
```
logs/
├── movex-api-2026-01-10.txt    (auto-deleted after 30 days)
├── movex-api-2026-01-11.txt
├── movex-api-2026-01-12.txt    (current day)
└── movex-api-2026-01-13.txt    (tomorrow)
```

## Build Status
✅ **Build Succeeded** with 0 errors, 59 warnings (all pre-existing nullable warnings)

## Next Steps
1. Deploy to SRXWEBAPP1 (IIS)
2. Configure `Logging__LogLevel__Default` environment variable in IIS App Settings
3. Monitor `logs/` directory for audit trail
4. Implement real Polly retry/circuit breaker policies (currently NoOp)
5. Add metrics/APM integration (e.g., Application Insights)
