using System.Collections.Generic;
using Movex.API.Core.Models;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for managing transaction configurations
    /// </summary>
    public interface ITransactionConfigurationService
    {
        /// <summary>
        /// Get all available programs
        /// </summary>
        List<string> GetPrograms();

        /// <summary>
        /// Get program configuration
        /// </summary>
        ProgramConfig? GetProgram(string programName);

        /// <summary>
        /// Get transaction definition
        /// </summary>
        TransactionDefinition? GetTransaction(string program, string transaction);

        /// <summary>
        /// Get all transactions for a program
        /// </summary>
        List<TransactionDefinition> GetTransactions(string program);
    }
}
