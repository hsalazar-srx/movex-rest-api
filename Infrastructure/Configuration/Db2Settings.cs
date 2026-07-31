// Infrastructure/Configuration/Db2Settings.cs
// DB2/AS400 direct-query configuration for custom endpoints that have no M3 MI equivalent.
// Maps to appsettings.json["Db2"] section. Secrets stored via dotnet user-secrets (dev)
// or environment variables (prod).
namespace Movex.API.Infrastructure.Configuration;

public class Db2Settings
{
    /// <summary>
    /// IBM i ODBC connection string.
    /// Example: "Driver={IBM i Access ODBC Driver};System=192.168.1.x;Uid=USER;Pwd=PASS;"
    /// Set via user-secrets in dev, environment variable DB2__CONNECTIONSTRING in prod.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Schema that maps to MOVEX company 100 (production). Default: MVXCDTA.
    /// </summary>
    public string SchemaCmp100 { get; set; } = "MVXCDTA";

    /// <summary>
    /// Schema that maps to MOVEX company 300 (dev/UAT). Default: MVXC300.
    /// </summary>
    public string SchemaCmp300 { get; set; } = "MVXC300";

    /// <summary>
    /// Schema for Stargile custom tables (ZECNCIRF, ZECNMPMS) for company 100 (production).
    /// Stargile uses COMCDTA{CONO} libraries, mirroring M3's per-company library pattern.
    /// </summary>
    public string SchemaStargile100 { get; set; } = "COMCDTA100";

    /// <summary>
    /// Schema for Stargile custom tables (ZECNCIRF, ZECNMPMS) for company 300 (dev/UAT).
    /// </summary>
    public string SchemaStargile300 { get; set; } = "COMCDTA300";

    /// <summary>
    /// SQL command timeout in seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
