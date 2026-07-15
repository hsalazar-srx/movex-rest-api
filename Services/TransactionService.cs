using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;
using Polly;

namespace Movex.API.Services
{
    /// <summary>
    /// Service for executing M3 transactions (config-driven only)
    /// </summary>
    public class TransactionService : ITransactionService
    {
        private readonly MvxConnectionPool _connectionPool;
        private readonly ITransactionConfigurationService _configService;
        private readonly ITransactionStringBuilder _stringBuilder;
        private readonly IResponseParser _responseParser;
        private readonly ILogger<TransactionService> _logger;
        private readonly IAsyncPolicy _resiliencePolicy;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;

        public TransactionService(
            MvxConnectionPool connectionPool,
            ITransactionConfigurationService configService,
            ITransactionStringBuilder stringBuilder,
            IResponseParser responseParser,
            ILogger<TransactionService> logger,
            IAsyncPolicy resiliencePolicy,
            string host,
            int port,
            string username,
            string password)
        {
            _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _stringBuilder = stringBuilder ?? throw new ArgumentNullException(nameof(stringBuilder));
            _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        /// <summary>
        /// Execute an M3 transaction asynchronously
        /// </summary>
        public async Task<object> ExecuteTransactionAsync(
            string program,
            string transaction,
            Dictionary<string, string> fieldValues,
            string? overrideUsername = null,
            string? overridePassword = null,
            bool rawMode = false)
        {
            if (string.IsNullOrWhiteSpace(program))
            {
                throw new ArgumentException("Program name cannot be empty", nameof(program));
            }

            if (string.IsNullOrWhiteSpace(transaction))
            {
                throw new ArgumentException("Transaction name cannot be empty", nameof(transaction));
            }

            if (fieldValues == null)
            {
                throw new ArgumentNullException(nameof(fieldValues));
            }

            // Get transaction definition from configuration
            TransactionDefinition? txDef = _configService.GetTransaction(program, transaction);

            if (txDef == null)
            {
                _logger.LogError("Transaction '{Program}/{Transaction}' not found in configuration", program, transaction);
                throw new ArgumentException($"Transaction '{program}/{transaction}' not found in configuration");
            }

            _logger.LogInformation("Executing transaction '{Program}/{Transaction}'", program, transaction);

            // Build transaction string
            string transactionString = _stringBuilder.BuildTransactionString(txDef, fieldValues);
            
            _logger.LogDebug("Transaction string built (length={Length}): {TransactionString}", transactionString.Length, transactionString);

            PooledConnection? pooled = null;
            try
            {
                // Acquire connection from pool
                pooled = await _connectionPool.AcquireConnectionAsync();
                
                // Use override credentials if provided
                var user = string.IsNullOrEmpty(overrideUsername) ? _username : overrideUsername;
                var pass = string.IsNullOrEmpty(overridePassword) ? _password : overridePassword;
                
                // Determine API program (use override from fieldValues or build from config)
                string apiProgram = BuildApiProgram(program, fieldValues);
                
                _logger.LogDebug("Sending request to {Host}:{Port} with API program {ApiProgram}", _host, _port, apiProgram);

                // Execute transaction with resilience policy (retry + circuit breaker)
                string rawResponse = await _resiliencePolicy.ExecuteAsync(
                    async context =>
                    {
                        return await pooled.Connection.SendReceiveAsync(
                            _host,
                            _port,
                            user,
                            pass,
                            transactionString,
                            apiProgram);
                    },
                    new Polly.Context { { "logger", _logger } });


                var safeRawResponse = rawResponse ?? string.Empty;
                string trimmedResponse = safeRawResponse.TrimStart();
                bool startsWithOk = trimmedResponse.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                bool startsWithNok = trimmedResponse.StartsWith("NOK", StringComparison.OrdinalIgnoreCase);

                if (startsWithNok)
                {
                    // For list transactions, NOK on the first call = no records found (empty result, not an error)
                    if (txDef.ReturnsList)
                    {
                        _logger.LogInformation("List transaction returned NOK (no records found)");
                        return new List<Dictionary<string, object?>>();
                    }
                    _logger.LogWarning("Transaction returned NOK prefix, returning raw response for error handling");
                    return rawResponse ?? string.Empty;
                }

                // Parse response if response fields are configured (skipped when rawMode=true)
                if (!rawMode && txDef.ResponseFields?.Any() == true)
                {
                    if (txDef.ReturnsList)
                    {
                        var allRecords = new List<Dictionary<string, object?>>();
                        string currentRaw = safeRawResponse;
                        int maxIterations = 500;
                        bool cursorExhausted = false;

                        for (int i = 0; i < maxIterations; i++)
                        {
                            string trimmed = currentRaw.TrimStart();

                            // M3 list protocol termination signals:
                            // - NOK = error / no records found
                            // - OK  = end-of-list (sent after the last record is repeated)
                            // Both mean: stop, do not add this buffer as a record.
                            if (trimmed.StartsWith("NOK", StringComparison.OrdinalIgnoreCase) ||
                                trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                            {
                                cursorExhausted = true;
                                break;
                            }

                            string dataPayload = currentRaw.Length > 15 ? currentRaw.Substring(15) : string.Empty;
                            if (string.IsNullOrWhiteSpace(dataPayload))
                            {
                                cursorExhausted = true;
                                break;
                            }

                            var parsed = _responseParser.ParseResponse(currentRaw, txDef.ResponseFields);
                            if (parsed.Count > 0)
                                allRecords.Add(parsed);

                            string nextRaw = await pooled.Connection.SendReceiveNextAsync(transactionString) ?? string.Empty;
                            if (string.IsNullOrEmpty(nextRaw))
                            {
                                cursorExhausted = true;
                                break;
                            }
                            currentRaw = nextRaw;
                        }

                        // Always discard list connections — M3 sockets retain list session
                        // state even after the OK terminator, and will respond REP (not OK)
                        // to the next transaction on the same socket, silently dropping writes.
                        pooled.IsHealthy = false;

                        _logger.LogInformation("List transaction completed, {RecordCount} records", allRecords.Count);
                        return allRecords;
                    }
                    else
                    {
                        var parsedResponse = _responseParser.ParseResponse(safeRawResponse, txDef.ResponseFields);
                        bool startsWithRep = safeRawResponse.TrimStart().StartsWith("REP", StringComparison.OrdinalIgnoreCase);
                        // OK = explicit success; REP = data record returned — both indicate success for single-record calls
                        if (startsWithOk || startsWithRep)
                        {
                            EnsureM3SuccessFields(parsedResponse);
                        }
                        _logger.LogInformation("Transaction completed successfully, parsed {FieldCount} response fields", parsedResponse.Count);
                        return parsedResponse;
                    }
                }

                // Raw mode on a list transaction leaves the M3 cursor mid-stream.
                // Mark the connection unhealthy so it is not reused with stale cursor state.
                if (txDef.ReturnsList)
                    pooled.IsHealthy = false;

                _logger.LogInformation("Transaction completed successfully, response length: {Length}", safeRawResponse.Length);
                return rawResponse ?? string.Empty;
            }
            catch (Exception ex)
            {
                // Mark connection as unhealthy on error
                if (pooled != null)
                {
                    pooled.IsHealthy = false;
                }
                
                _logger.LogError(ex, "Transaction '{Program}/{Transaction}' failed: {Message}", program, transaction, ex.Message);
                throw new Exception($"M3 transaction error: {ex.Message}", ex);
            }
            finally
            {
                // Always return connection to pool
                pooled?.Dispose();
            }
        }

        /// <summary>
        /// Build API program string (LIBRARY/PROGRAM format)
        /// </summary>
        private string BuildApiProgram(string program, Dictionary<string, string> fieldValues)
        {
            // Check if APIPROGRAM is provided in field values
            if (fieldValues.TryGetValue("APIPROGRAM", out var apiProgram) && 
                !string.IsNullOrWhiteSpace(apiProgram))
            {
                return apiProgram;
            }

            // Default format: MVXCOBJ/PROGRAM
            return $"MVXCOBJ/{program}";
        }

        private static void EnsureM3SuccessFields(Dictionary<string, object?> parsedResponse)
        {
            if (parsedResponse == null)
            {
                return;
            }

            // Always overwrite — the parser may have read "OK " from the buffer into MSID
            // when responseFields are defined but the response is just an OK/NOK prefix.
            parsedResponse["MSID"] = "000";

            if (!parsedResponse.ContainsKey("MSDT"))
            {
                parsedResponse["MSDT"] = "OK";
            }
        }

        /// <summary>
        /// Get connection pool statistics
        /// </summary>
        public ConnectionPoolStats GetPoolStats()
        {
            // _connectionPool is required via DI and cannot be null
            return _connectionPool.GetStats();
        }
    }
}
