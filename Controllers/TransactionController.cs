// Controllers/TransactionController.cs - REST API Controller for M3 transactions
using Microsoft.AspNetCore.Mvc;

using Movex.API.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Movex.API.Controllers
{
    [ApiController]
    [Route("api")]
    public class TransactionController : ControllerBase
    {
        private readonly ITransactionService _service;
        private readonly ITransactionConfigurationService _configService;
        private readonly IItemValidationService _validationService;
        private readonly ILogger<TransactionController> _logger;
        private readonly IConfiguration _configuration;

        public TransactionController(
            ITransactionService service,
            ITransactionConfigurationService configService,
            IItemValidationService validationService,
            ILogger<TransactionController> logger,
            IConfiguration configuration)
        {
            _service = service;
            _configService = configService;
            _validationService = validationService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Check if M3 response indicates success (starts with "OK") or failure (starts with "NOK")
        /// Extracts MSID (error code) and MSDT (error details) from parsed responses
        /// </summary>
        private bool IsM3ResponseSuccessful(object? response, out string? errorMessage, out string? errorCode)
        {
            errorMessage = null;
            errorCode = null;
            
            if (response == null)
            {
                errorMessage = "No response from M3";
                errorCode = "M3_NULL_RESPONSE";
                return false;
            }
            
            // Check if response is a string (raw response)
            if (response is string rawResponse)
            {
                if (rawResponse.StartsWith("NOK", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = rawResponse.Length > 4 ? rawResponse.Substring(4).Trim() : "Transaction failed";
                    errorCode = "M3_NOK_RESPONSE";
                    return false;
                }
                return rawResponse.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
            }

            if (response is List<Dictionary<string, object?>> parsedList)
            {
                return EvaluateParsedList(parsedList, out errorMessage, out errorCode);
            }

            if (response is List<Dictionary<string, object>> parsedListNonNullable)
            {
                var convertedList = parsedListNonNullable
                    .Select(record => record.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                    .ToList();
                return EvaluateParsedList(convertedList, out errorMessage, out errorCode);
            }
            
            // For parsed responses (Dictionary), check for MSID field
            if (response is Dictionary<string, object?> parsedResponse)
            {
                return EvaluateParsedResponse(parsedResponse, out errorMessage, out errorCode);
            }

            if (response is Dictionary<string, object> parsedResponseNonNullable)
            {
                var converted = parsedResponseNonNullable.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
                return EvaluateParsedResponse(converted, out errorMessage, out errorCode);
            }
            
            // CHANGED: Log the actual response type and don't assume success
            _logger.LogWarning("M3 response has no MSID field - cannot verify transaction success. Response type: {Type}, Content: {Content}", 
                response.GetType().Name,
                System.Text.Json.JsonSerializer.Serialize(response));
            
            // IMPORTANT: Return false here - don't assume success!
            // The transaction likely failed silently
            errorMessage = "M3 returned response without MSID field - unable to confirm success";
            errorCode = "M3_NO_MSID_FIELD";
            return false;
        }

        private bool EvaluateParsedList(List<Dictionary<string, object?>> parsedList, out string? errorMessage, out string? errorCode)
        {
            errorMessage = null;
            errorCode = null;

            if (parsedList.Count == 0)
            {
                errorMessage = "M3 returned empty response - transaction may have failed";
                errorCode = "M3_EMPTY_RESPONSE";
                _logger.LogWarning("M3 returned empty parsed list response - this may indicate a rejected transaction");
                return false;
            }

            return EvaluateParsedResponse(parsedList[0], out errorMessage, out errorCode);
        }

        private bool EvaluateParsedResponse(Dictionary<string, object?> parsedResponse, out string? errorMessage, out string? errorCode)
        {
            errorMessage = null;
            errorCode = null;

            if (parsedResponse.TryGetValue("MSID", out var msidObj))
            {
                string? msid = msidObj?.ToString()?.Trim();

                // MSID of "000" or empty typically indicates success
                if (string.IsNullOrEmpty(msid) || msid.Equals("000", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Extract error details from MSDT field if present
                if (parsedResponse.TryGetValue("MSDT", out var msdtObj))
                {
                    errorMessage = msdtObj?.ToString()?.Trim() ?? "M3 transaction failed";
                }
                else
                {
                    errorMessage = $"M3 transaction failed with error code: {msid}";
                }

                errorCode = msid;
                return false;
            }

            // If no MSID but response is empty, treat as failure
            if (parsedResponse.Count == 0)
            {
                errorMessage = "M3 returned empty response - transaction may have failed";
                errorCode = "M3_EMPTY_RESPONSE";
                _logger.LogWarning("M3 returned empty parsed response - this may indicate a rejected transaction");
                return false;
            }

            _logger.LogWarning("M3 response has no MSID field - cannot verify transaction success. Content: {Content}",
                System.Text.Json.JsonSerializer.Serialize(parsedResponse));
            errorMessage = "M3 returned response without MSID field - unable to confirm success";
            errorCode = "M3_NO_MSID_FIELD";
            return false;
        }
        /// <summary>
        /// Validate item and adjust stock in a single server-side flow (Option A)
        /// Validates item first, then delegates to the dynamic MMS310MI/Update endpoint
        /// </summary>
        [HttpPost("workflows/AdjustStockWithValidation")]
        public async Task<IActionResult> AdjustStockWithValidation([FromBody] Dictionary<string, object> request)
        {
            try
            {
                var company = _configuration["M3:DEFAULT_COMPANY"] ?? "300";
                
                // Extract inputs for validation
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? whlo = request.ContainsKey("WHLO") ? request["WHLO"]?.ToString() : null;
                string? whsl = request.ContainsKey("WHSL") ? request["WHSL"]?.ToString() : null;
                string? stqi = request.ContainsKey("STQI") ? request["STQI"]?.ToString() : null;

                // Validate required fields
                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(whlo) || 
                    string.IsNullOrWhiteSpace(whsl) || string.IsNullOrWhiteSpace(stqi))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Required fields: ITNO, WHLO, WHSL, STQI",
                        provided = new { ITNO = itno, WHLO = whlo, WHSL = whsl, STQI = stqi }
                    });
                }

                // Apply defaults for optional fields
                string? stagValue = request.ContainsKey("STAG") ? request["STAG"]?.ToString() : null;
                string stag = string.IsNullOrWhiteSpace(stagValue) ? "2" : stagValue; // Default to "2" (Approved)
                
                string? stdi = request.ContainsKey("STDI") && !string.IsNullOrWhiteSpace(request["STDI"]?.ToString())
                    ? request["STDI"]?.ToString()
                    : DateTime.Now.ToString("yyyyMMdd"); // Default to current date

                // Validate STAG (Item Status: 1=Under Inspection, 2=Approved, 3=Rejected)
                if (!new[] { "1", "2", "3" }.Contains(stag))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "STAG must be 1 (Under Inspection), 2 (Approved), or 3 (Rejected)",
                        provided = stag
                    });
                }

                _logger.LogInformation("[AdjustStockWithValidation] Processing: ITNO={ITNO}, WHLO={WHLO}, WHSL={WHSL}, STQI={STQI}, Company={CONO}", 
                    itno, whlo, whsl, stqi, company);

                // Step 1: Validate item status/type via ItemValidationService
                var validation = await _validationService.ValidateItemAsync(company, itno);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("[AdjustStockWithValidation] Validation failed: {ErrorCode} - {ErrorMessage}", 
                        validation.ErrorCode, validation.ErrorMessage);
                    
                    return StatusCode(422, new
                    {
                        success = false,
                        errorCode = validation.ErrorCode,
                        error = validation.ErrorMessage,
                        stat = validation.Status,
                        itty = validation.ItemType
                    });
                }

                _logger.LogInformation("[AdjustStockWithValidation] Item validated successfully: STAT={STAT}, ITTY={ITTY}", 
                    validation.Status, validation.ItemType);

                // Step 2: Build request for MMS310MI.Update with all fields (including defaults)
                var updateRequest = new Dictionary<string, object>
                {
                    { "CONO", company },
                    { "WHLO", whlo! },
                    { "ITNO", itno! },
                    { "WHSL", whsl! },
                    { "STQI", stqi! },
                    { "STAG", stag },
                    { "STDI", stdi! }
                };

                // Add optional traceability fields if provided
                if (request.ContainsKey("BREF") && !string.IsNullOrWhiteSpace(request["BREF"]?.ToString()))
                {
                    updateRequest["BREF"] = request["BREF"]!;
                }
                if (request.ContainsKey("BREM") && !string.IsNullOrWhiteSpace(request["BREM"]?.ToString()))
                {
                    updateRequest["BREM"] = request["BREM"]!;
                }

                _logger.LogInformation("[AdjustStockWithValidation] Delegating to dynamic MMS310MI/Update endpoint");

                // Step 3: Call the dynamic ExecuteTransaction endpoint internally
                // This will handle field validation, QLUN retrieval, padding, and proper M3 response parsing
                var updateResult = await ExecuteTransaction("MMS310MI", "Update", updateRequest);

                // Step 4: Check if the update succeeded
                if (updateResult is OkObjectResult okResult)
                {
                    // Extract the response data
                    var responseValue = okResult.Value;
                    
                    // Use reflection to get the 'data' property from the anonymous type
                    var dataProperty = responseValue?.GetType().GetProperty("data");
                    var updateData = dataProperty?.GetValue(responseValue);

                    _logger.LogInformation("[AdjustStockWithValidation] Stock updated successfully for item {ITNO}", itno);

                    return Ok(new
                    {
                        success = true,
                        program = "MMS310MI",
                        transaction = "Update",
                        validation = new { stat = validation.Status, itty = validation.ItemType },
                        data = updateData,
                        timestamp = DateTime.UtcNow
                    });
                }
                else if (updateResult is StatusCodeResult statusCodeResult)
                {
                    // Handle status code-only responses (unlikely for ExecuteTransaction)
                    _logger.LogWarning("[AdjustStockWithValidation] MMS310MI/Update returned status code {StatusCode}", 
                        statusCodeResult.StatusCode);
                    
                    return StatusCode(statusCodeResult.StatusCode, new
                    {
                        success = false,
                        error = "Stock update failed with no error details",
                        program = "MMS310MI",
                        transaction = "Update"
                    });
                }
                else if (updateResult is ObjectResult objectResult)
                {
                    // Handle error responses (422, 500, etc.)
                    _logger.LogWarning("[AdjustStockWithValidation] MMS310MI/Update failed with status {StatusCode}", 
                        objectResult.StatusCode);
                    
                    return StatusCode(objectResult.StatusCode ?? 500, objectResult.Value);
                }
                else
                {
                    // Unexpected result type
                    _logger.LogError("[AdjustStockWithValidation] Unexpected result type from ExecuteTransaction: {Type}", 
                        updateResult?.GetType().Name ?? "null");
                    
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Unexpected error during stock update",
                        program = "MMS310MI",
                        transaction = "Update"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AdjustStockWithValidation] Error: {Message}", ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// MES Stock Management System (SMS) - Requirement 1
        /// Validates item, checks warehouse balance, and updates stock
        /// Delegates to dynamic transaction endpoints for consistent behavior
        /// </summary>
        [HttpPost("workflows/StockManagement")]
        public async Task<IActionResult> StockManagement([FromBody] Dictionary<string, object> request)
        {
            try
            {
                var company = _configuration["M3:DEFAULT_COMPANY"] ?? "300";
                
                // Extract inputs
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? whlo = request.ContainsKey("WHLO") ? request["WHLO"]?.ToString() : null;
                string? whsl = request.ContainsKey("WHSL") ? request["WHSL"]?.ToString()?.PadRight(10) : null;
                string? newQty = request.ContainsKey("STQI") ? request["STQI"]?.ToString() : null;

                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(whlo) || 
                    string.IsNullOrWhiteSpace(whsl) || string.IsNullOrWhiteSpace(newQty))
                {
                    return BadRequest(new { success = false, error = "Required fields: ITNO, WHLO, WHSL, STQI" });
                }

                _logger.LogInformation("[StockManagement] Processing: ITNO={ITNO}, WHLO={WHLO}, WHSL={WHSL}, STQI={STQI}, Company={CONO}", 
                    itno, whlo, whsl, newQty, company);

                // Step 1: Validate item (STAT 20/50, ITTY 030)
                var validation = await _validationService.ValidateItemAsync(company, itno);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("[StockManagement] Validation failed: {ErrorCode} - {ErrorMessage}", 
                        validation.ErrorCode, validation.ErrorMessage);
                    return StatusCode(422, new { success = false, errorCode = validation.ErrorCode, error = validation.ErrorMessage });
                }

                // Step 2: Check warehouse balance using ExecuteTransaction
                var balanceRequest = new Dictionary<string, object>
                {
                    { "CONO", company },
                    { "WHLO", whlo },
                    { "ITNO", itno }
                };

                _logger.LogInformation("[StockManagement] Checking warehouse balance via MMS200MI/GetItmWhsBal");
                var balanceResult = await ExecuteTransaction("MMS200MI", "GetItmWhsBal", balanceRequest);
                
                if (balanceResult is not OkObjectResult balanceOk)
                {
                    _logger.LogError("[StockManagement] GetItmWhsBal failed");
                    return StatusCode(502, new { success = false, error = "Unable to retrieve warehouse balance" });
                }

                var balanceResponseValue = balanceOk.Value;
                var balanceDataProperty = balanceResponseValue?.GetType().GetProperty("data");
                var balanceData = balanceDataProperty?.GetValue(balanceResponseValue) as Dictionary<string, object>;

                decimal onHandBalance = balanceData != null && balanceData.TryGetValue("STQT", out var stqtObj) 
                    ? Convert.ToDecimal(stqtObj) : 0;
                decimal newQtyDecimal = decimal.Parse(newQty);

                if (onHandBalance < newQtyDecimal)
                {
                    _logger.LogWarning("[StockManagement] Insufficient stock: On-hand={OnHand}, Requested={Requested}", 
                        onHandBalance, newQtyDecimal);
                    
                    return StatusCode(422, new
                    {
                        success = false,
                        errorCode = "004",
                        error = "Insufficient available stock in warehouse",
                        onHandBalance,
                        requestedQuantity = newQtyDecimal
                    });
                }

                // Step 3: Update stock using ExecuteTransaction with optional traceability fields
                var updateRequest = new Dictionary<string, object>
                {
                    { "CONO", company },
                    { "WHLO", whlo },
                    { "ITNO", itno },
                    { "WHSL", whsl },
                    { "STQI", newQty }
                };

                // Add traceability fields - prefer BREF if both provided, but support both
                if (request.ContainsKey("BREF") && !string.IsNullOrWhiteSpace(request["BREF"]?.ToString()))
                {
                    updateRequest["BREF"] = request["BREF"]!;
                }
                else if (request.ContainsKey("BREM") && !string.IsNullOrWhiteSpace(request["BREM"]?.ToString()))
                {
                    updateRequest["BREM"] = request["BREM"]!;
                }
                else
                {
                    // Default to "MES SMS" if neither provided
                    updateRequest["BREF"] = "MES SMS";
                }

                _logger.LogInformation("[StockManagement] Delegating to dynamic MMS310MI/Update endpoint");
                var updateResult = await ExecuteTransaction("MMS310MI", "Update", updateRequest);

                if (updateResult is OkObjectResult updateOk)
                {
                    var updateResponseValue = updateOk.Value;
                    var updateDataProperty = updateResponseValue?.GetType().GetProperty("data");
                    var updateData = updateDataProperty?.GetValue(updateResponseValue);

                    _logger.LogInformation("[StockManagement] Stock updated successfully for item {ITNO}", itno);

                    return Ok(new
                    {
                        success = true,
                        program = "MMS310MI",
                        transaction = "Update",
                        validation = new { stat = validation.Status, itty = validation.ItemType },
                        balanceCheck = new { onHandBalance, newQuantity = newQtyDecimal },
                        data = updateData,
                        timestamp = DateTime.UtcNow
                    });
                }
                else if (updateResult is ObjectResult errorResult)
                {
                    _logger.LogWarning("[StockManagement] MMS310MI/Update failed with status {StatusCode}", 
                        errorResult.StatusCode);
                    return StatusCode(errorResult.StatusCode ?? 500, errorResult.Value);
                }
                else
                {
                    _logger.LogError("[StockManagement] Unexpected result type from ExecuteTransaction: {Type}", 
                        updateResult?.GetType().Name ?? "null");
                    return StatusCode(500, new { success = false, error = "Unexpected error during stock update" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StockManagement] Error: {Message}", ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// MES Check Standard Cost - Requirement 2
        /// Validates item and retrieves unit cost price
        /// </summary>
        [HttpPost("workflows/GetStandardCost")]
        public async Task<IActionResult> GetStandardCost([FromBody] Dictionary<string, object> request)
        {
            try
            {
                var company = _configuration["M3:DEFAULT_COMPANY"] ?? "300";
                
                // Extract inputs
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? faci = request.ContainsKey("FACI") ? request["FACI"]?.ToString() : null;

                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(faci))
                {
                    return BadRequest(new { success = false, error = "Required fields: ITNO, FACI" });
                }

                _logger.LogInformation("[GetStandardCost] Processing: ITNO={ITNO}, FACI={FACI}, Company={CONO}", itno, faci, company);

                // Step 1: Validate item (STAT 20/50, ITTY 030)
                var validation = await _validationService.ValidateItemAsync(company, itno);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("[GetStandardCost] Validation failed: {ErrorCode} - {ErrorMessage}", validation.ErrorCode, validation.ErrorMessage);
                    return StatusCode(422, new { success = false, errorCode = validation.ErrorCode, error = validation.ErrorMessage });
                }

                // Step 2: Get facility cost and details via MMS200MI.GetItmFac
                var costFields = new Dictionary<string, string>
                {
                    { "CONO", company },
                    { "FACI", faci ?? string.Empty },
                    { "ITNO", itno ?? string.Empty }
                };

                var costResponse = await _service.ExecuteTransactionAsync("MMS200MI", "GetItmFac", costFields) as Dictionary<string, object>;
                if (costResponse == null)
                {
                    _logger.LogError("[GetStandardCost] MMS200MI.GetItmFac returned empty response - facility may not exist");
                    return StatusCode(422, new { success = false, errorCode = "011", error = "Facility Does Not Exist" });
                }

                decimal unitCost = costResponse.TryGetValue("UCOS", out var ucosObj) ? Convert.ToDecimal(ucosObj) : 0;
                string? facilityName = costResponse.TryGetValue("FACN", out var facnObj) ? facnObj?.ToString() : null;
                string? itemDescription = costResponse.TryGetValue("ITDS", out var itdsObj) ? itdsObj?.ToString() : null;

                _logger.LogInformation("[GetStandardCost] Retrieved unit cost {UnitCost} for item {ITNO} at facility {FACI}", unitCost, itno, faci);

                return Ok(new
                {
                    success = true,
                    program = "MMS200MI",
                    transaction = "GetItmFac",
                    validation = new { stat = validation.Status, itty = validation.ItemType },
                    data = new { unitCostPrice = unitCost, facility = faci, facilityName = facilityName, itemNumber = itno, itemDescription = itemDescription },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GetStandardCost] Error: {Message}", ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// List all available M3 programs
        /// </summary>
        [HttpGet("programs")]
        public IActionResult GetPrograms()
        {
            var programs = _configService.GetPrograms();
            return Ok(new
            {
                success = true,
                count = programs.Count,
                programs = programs
            });
        }

        /// <summary>
        /// Get program metadata
        /// </summary>
        [HttpGet("programs/{program}")]
        public IActionResult GetProgram(string program)
        {
            var config = _configService.GetProgram(program);
            if (config == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = $"Program not found: {program}"
                });
            }

            return Ok(new
            {
                success = true,
                program = config.Program,
                library = config.Library,
                description = config.Description,
                transactionCount = config.Transactions.Count,
                transactions = config.Transactions.Select(t => t.Name)
            });
        }

        /// <summary>
        /// Get transaction information for a program
        /// </summary>
        [HttpGet("{program}/{transaction}/info")]
        public IActionResult GetTransactionInfo(string program, string transaction)
        {
            var txDef = _configService.GetTransaction(program, transaction);
            if (txDef == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = $"Transaction '{transaction}' not found in program '{program}'"
                });
            }

            return Ok(new
            {
                success = true,
                program = program,
                transaction = txDef.Name,
                description = txDef.Description,
                fields = txDef.Fields.Select(f => new
                {
                    name = f.Name,
                    position = f.Position,
                    length = f.Length,
                    type = f.Type,
                    required = f.Required,
                    description = f.Description
                }),
                responseFields = txDef.ResponseFields?.Select(f => new
                {
                    name = f.Name,
                    position = f.Position,
                    length = f.Length,
                    type = f.Type,
                    description = f.Description
                })
            });
        }

        /// <summary>
        /// Execute transaction for a program (POST body)
        /// </summary>
        [HttpPost("{program}/{transaction}")]
        public async Task<IActionResult> ExecuteTransaction(string program, string transaction, [FromBody] Dictionary<string, object> request)
        {
            try
            {
                // Validate method configuration
                var txDef = _configService.GetTransaction(program, transaction);
                if (txDef == null)
                {
                    return NotFound(new { success = false, error = $"Transaction '{transaction}' not found in program '{program}'" });
                }
                if (!string.IsNullOrEmpty(txDef.HttpMethod) && txDef.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { success = false, error = "Transaction is configured for GET. Use the GET endpoint with query parameters." });
                }

                // Extract credentials if provided
                string? username = null;
                string? password = null;
                
                if (request.ContainsKey("username"))
                {
                    username = request["username"]?.ToString();
                    request.Remove("username");
                }
                if (request.ContainsKey("password"))
                {
                    password = request["password"]?.ToString();
                    request.Remove("password");
                }

                // Convert field values to string dictionary
                var fieldValues = new Dictionary<string, string>();
                foreach (var kvp in request)
                {
                    fieldValues[kvp.Key] = kvp.Value?.ToString() ?? "";
                }

                // Execute transaction
                var response = await _service.ExecuteTransactionAsync(program, transaction, fieldValues, username, password);

                object responseData = response switch
                {
                    Dictionary<string, object?> dict => dict,
                    List<Dictionary<string, object?>> list => new { records = list, count = list.Count },
                    _ => new { raw = response?.ToString() }
                };

                // Check if M3 response indicates success
                bool isSuccess = IsM3ResponseSuccessful(response, out string? m3Error, out string? m3ErrorCode);
                
                if (!isSuccess)
                {
                    _logger.LogWarning("[ExecuteTransaction] M3 returned NOK for {Program}/{Transaction}: {Error}", program, transaction, m3Error);
                    return StatusCode(422, new
                    {
                        success = false,
                        error = m3Error ?? "Transaction failed",
                        program = program,
                        transaction = transaction
                    });
                }

                return Ok(new
                {
                    success = true,
                    program = program,
                    transaction = transaction,
                    data = responseData,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    error = ex.Message,
                    program = program,
                    transaction = transaction
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] Error: {ex.Message}\n{ex}");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    program = program,
                    transaction = transaction
                });
            }
        }

        /// <summary>
        /// Execute transaction for a program (GET query parameters)
        /// </summary>
        [HttpGet("{program}/{transaction}")]
        public async Task<IActionResult> ExecuteTransactionGet(string program, string transaction)
        {
            try
            {
                var txDef = _configService.GetTransaction(program, transaction);
                if (txDef == null)
                {
                    return NotFound(new { success = false, error = $"Transaction '{transaction}' not found in program '{program}'" });
                }
                if (!string.Equals(txDef.HttpMethod ?? "POST", "GET", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { success = false, error = "Transaction is not configured for GET. Use POST with a JSON body." });
                }

                // Bind query parameters to field values
                var fieldValues = Request.Query.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());

                var response = await _service.ExecuteTransactionAsync(program, transaction, fieldValues);

                object responseData = response switch
                {
                    Dictionary<string, object> dict => dict,
                    List<Dictionary<string, object>> list => new { records = list, count = list.Count },
                    _ => new { raw = response?.ToString() }
                };

                // Check if M3 response indicates success
                bool isSuccess = IsM3ResponseSuccessful(response, out string? m3Error, out string? m3ErrorCode);
                
                if (!isSuccess)
                {
                    _logger.LogWarning("[ExecuteTransactionGet] M3 returned NOK for {Program}/{Transaction}: {Error}", program, transaction, m3Error);
                    return StatusCode(422, new
                    {
                        success = false,
                        error = m3Error ?? "Transaction failed",
                        program = program,
                        transaction = transaction
                    });
                }

                return Ok(new
                {
                    success = true,
                    program = program,
                    transaction = transaction,
                    data = responseData,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] GET Error: {ex.Message}\n{ex}");
                return StatusCode(500, new { success = false, error = ex.Message, program, transaction });
            }
        }

        #region MMS175MI - Movement (Change Location)

        /// <summary>
        /// MMS175MI - Update (Change item location)
        /// Transfers item quantity from one location to another within same warehouse
        /// </summary>
        [HttpPost("mms175mi/update")]
        public async Task<IActionResult> MMS175MI_Update([FromBody] Dictionary<string, object> request)
        {
            try
            {
                var company = _configuration["M3:DEFAULT_COMPANY"] ?? "300";

                // Extract required fields
                string? cono = request.ContainsKey("CONO") ? request["CONO"]?.ToString() : company;
                string? whlo = request.ContainsKey("WHLO") ? request["WHLO"]?.ToString() : null;
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? twsl = request.ContainsKey("TWSL") ? request["TWSL"]?.ToString() : null;
                string? trqt = request.ContainsKey("TRQT") ? request["TRQT"]?.ToString() : null;
                string? whsl = request.ContainsKey("WHSL") ? request["WHSL"]?.ToString() : null;
                string bano = request.ContainsKey("BANO") ? request["BANO"]?.ToString() ?? "" : "";
                string camu = request.ContainsKey("CAMU") ? request["CAMU"]?.ToString() ?? "" : "";

                // Validate required fields
                var missingFields = new Dictionary<string, bool>
                {
                    { "WHLO", string.IsNullOrWhiteSpace(whlo) },
                    { "ITNO", string.IsNullOrWhiteSpace(itno) },
                    { "TWSL", string.IsNullOrWhiteSpace(twsl) },
                    { "TRQT", string.IsNullOrWhiteSpace(trqt) },
                    { "WHSL", string.IsNullOrWhiteSpace(whsl) }
                };

                var missingFieldsList = missingFields.Where(f => f.Value).Select(f => f.Key).ToList();
                if (missingFieldsList.Any())
                {
                    _logger.LogWarning("[MMS175MI_Update] Missing required fields: {MissingFields}", string.Join(", ", missingFieldsList));
                    return BadRequest(new
                    {
                        success = false,
                        errorCode = "VALIDATION_MISSING_FIELDS",
                        error = "Required fields missing",
                        missingFields = missingFieldsList,
                        fieldDescriptions = new
                        {
                            WHLO = "From warehouse (3 chars)",
                            ITNO = "Item number (1-15 chars)",
                            TWSL = "To location (1-10 chars)",
                            TRQT = "Transfer quantity (positive decimal)",
                            WHSL = "From location (1-10 chars)"
                        },
                        timestamp = DateTime.UtcNow.ToString("o")
                    });
                }

                // Validate quantity is numeric and positive
                if (!decimal.TryParse(trqt, out decimal quantity) || quantity <= 0)
                {
                    _logger.LogWarning("[MMS175MI_Update] Invalid quantity value: {TRQT}", trqt);
                    return BadRequest(new
                    {
                        success = false,
                        errorCode = "VALIDATION_INVALID_QUANTITY",
                        error = "TRQT (quantity) must be a positive number",
                        provided = trqt,
                        timestamp = DateTime.UtcNow.ToString("o")
                    });
                }

                _logger.LogInformation("[MMS175MI_Update] Processing location change: ITNO={ITNO}, WHLO={WHLO}, WHSL={WHSL} -> TWSL={TWSL}, QTY={TRQT}", 
                    itno, whlo, whsl, twsl, quantity);

                var fields = new Dictionary<string, string>
                {
                    { "CONO", cono! },
                    { "WHLO", whlo! },
                    { "ITNO", itno! },
                    { "TWSL", twsl! },
                    { "TRQT", trqt! },
                    { "WHSL", whsl! },
                    { "BANO", bano ?? "" },
                    { "CAMU", camu ?? "" }
                };

                var response = await _service.ExecuteTransactionAsync("MMS175MI", "Update", fields) as Dictionary<string, object>;

                // Check if M3 response indicates success, extract error code if NOK
                bool isSuccess = IsM3ResponseSuccessful(response, out string? m3Error, out string? errorCode);

                if (!isSuccess)
                {
                    _logger.LogWarning("[MMS175MI_Update] M3 returned NOK response - Code: {ErrorCode}, Message: {Error}", errorCode, m3Error);
                    return StatusCode(422, new
                    {
                        success = false,
                        errorCode = errorCode ?? "M3_ERROR",
                        error = m3Error ?? "Location change failed",
                        program = "MMS175MI",
                        transaction = "Update",
                        details = new
                        {
                            itemNumber = itno,
                            fromWarehouse = whlo,
                            fromLocation = whsl,
                            toLocation = twsl,
                            requestedQuantity = quantity
                        },
                        timestamp = DateTime.UtcNow.ToString("o")
                    });
                }

                object responseData = response ?? (object)new { message = "Location change completed" };

                _logger.LogInformation("[MMS175MI_Update] Location change completed successfully for ITNO={ITNO}, moved {QTY} units from {FROM_LOC} to {TO_LOC}",
                    itno, quantity, whsl, twsl);

                return Ok(new
                {
                    success = true,
                    program = "MMS175MI",
                    transaction = "Update",
                    data = new
                    {
                        itemNumber = itno,
                        fromWarehouse = whlo,
                        fromLocation = whsl,
                        toLocation = twsl,
                        quantity = quantity,
                        lot = bano,
                        container = camu,
                        m3ErrorCode = "000",
                        m3Response = responseData
                    },
                    timestamp = DateTime.UtcNow.ToString("o")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MMS175MI_Update] Unexpected error: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    success = false,
                    errorCode = "SYSTEM_ERROR",
                    error = "An unexpected error occurred while processing the location change",
                    details = new
                    {
                        exceptionType = ex.GetType().Name,
                        message = ex.Message,
                        timestamp = DateTime.UtcNow.ToString("o")
                    },
                    program = "MMS175MI",
                    transaction = "Update"
                });
            }
        }

        #endregion

    }
}
