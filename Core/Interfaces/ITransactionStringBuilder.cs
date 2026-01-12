using System.Collections.Generic;
using Movex.API.Core.Models;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for building M3 transaction strings from configuration and field values
    /// </summary>
    public interface ITransactionStringBuilder
    {
        /// <summary>
        /// Build a transaction string from transaction definition and field values
        /// </summary>
        /// <param name="definition">Transaction definition from configuration</param>
        /// <param name="fieldValues">Dictionary of field names/positions to values</param>
        /// <returns>Formatted transaction string for M3</returns>
        string BuildTransactionString(TransactionDefinition definition, Dictionary<string, string> fieldValues);
    }
}
