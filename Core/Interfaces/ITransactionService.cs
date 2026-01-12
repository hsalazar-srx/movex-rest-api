using System.Collections.Generic;
using System.Threading.Tasks;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for executing M3 transactions
    /// </summary>
    public interface ITransactionService
    {
        /// <summary>
        /// Execute an M3 transaction asynchronously
        /// </summary>
        /// <param name="program">M3 program name (e.g., MMS200MI)</param>
        /// <param name="transaction">Transaction name (e.g., GetItmBasic)</param>
        /// <param name="fieldValues">Dictionary of field names/positions to values</param>
        /// <param name="overrideUsername">Optional username override</param>
        /// <param name="overridePassword">Optional password override</param>
        /// <returns>Parsed response (Dictionary or raw string)</returns>
        Task<object> ExecuteTransactionAsync(
            string program,
            string transaction,
            Dictionary<string, string> fieldValues,
            string? overrideUsername = null,
            string? overridePassword = null);

        /// <summary>
        /// Get connection pool statistics
        /// </summary>
        ConnectionPoolStats GetPoolStats();
    }
}
