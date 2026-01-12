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

        public TransactionController(
            ITransactionService service,
            ITransactionConfigurationService configService,
            IItemValidationService validationService,
            ILogger<TransactionController> logger)
        {
            _service = service;
            _configService = configService;
            _validationService = validationService;
            _logger = logger;
        }

        /// <summary>
        /// Validate item and adjust stock in a single server-side flow (Option A)
        /// </summary>
        [HttpPost("workflows/AdjustStockWithValidation")]
        public async Task<IActionResult> AdjustStockWithValidation([FromBody] Dictionary<string, object> request)
        {
            try
            {
                // Extract inputs
            string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
            string? whlo = request.ContainsKey("WHLO") ? request["WHLO"]?.ToString() : null;
            string? whsl = request.ContainsKey("WHSL") ? request["WHSL"]?.ToString() : null;
            string? stqi = request.ContainsKey("STQI") ? request["STQI"]?.ToString() : null;
            string? brem = request.ContainsKey("BREM") ? request["BREM"]?.ToString() : "";

                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(whlo) || string.IsNullOrWhiteSpace(whsl) || string.IsNullOrWhiteSpace(stqi))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Required fields: ITNO, WHLO, WHSL, STQI",
                        provided = new { ITNO = itno, WHLO = whlo, WHSL = whsl, STQI = stqi }
                    });
                }

                // 1) Validate item status/type via ItemValidationService
                var validation = await _validationService.ValidateItemAsync("300", itno);
                if (!validation.IsValid)
                {
                    return StatusCode(422, new
                    {
                        success = false,
                        errorCode = validation.ErrorCode,
                        error = validation.ErrorMessage,
                        stat = validation.Status,
                        itty = validation.ItemType
                    });
                }

                // 2) Adjust stock via MMS310MI.Update (server-enforced company 300)
                var updateFields = new Dictionary<string, string>
                {
                    { "CONO", "300" },
                    { "WHLO", whlo ?? string.Empty },
                    { "ITNO", itno ?? string.Empty },
                    { "WHSL", whsl ?? string.Empty },
                    { "STQI", stqi ?? string.Empty },
                    { "BREM", brem ?? string.Empty }
                };

                var updateResponse = await _service.ExecuteTransactionAsync("MMS310MI", "Update", updateFields) as Dictionary<string, object>;

                object updateData = updateResponse ?? (object)new { message = "Update executed" };

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AdjustStockWithValidation] Error: {Message}", ex.Message);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// MES Stock Management System (SMS) - Requirement 1
        /// Validates item, checks warehouse balance, and updates stock
        /// </summary>
        [HttpPost("workflows/StockManagement")]
        public async Task<IActionResult> StockManagement([FromBody] Dictionary<string, object> request)
        {
            try
            {
                // Extract inputs
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? whlo = request.ContainsKey("WHLO") ? request["WHLO"]?.ToString() : null;
                string? whsl = request.ContainsKey("WHSL") ? request["WHSL"]?.ToString()?.PadRight(10) : null;
                string? newQty = request.ContainsKey("STQI") ? request["STQI"]?.ToString() : null;
                string brem = "MES SMS"; // Fixed remark per requirement

                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(whlo) || string.IsNullOrWhiteSpace(whsl) || string.IsNullOrWhiteSpace(newQty))
                {
                    return BadRequest(new { success = false, error = "Required fields: ITNO, WHLO, WHSL, STQI" });
                }

                _logger.LogInformation("[StockManagement] Processing: ITNO={ITNO}, WHLO={WHLO}, WHSL={WHSL}, STQI={STQI}", itno, whlo, whsl, newQty);

                // Step 1: Validate item (STAT 20/50, ITTY 030)
                var validation = await _validationService.ValidateItemAsync("300", itno);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("[StockManagement] Validation failed: {ErrorCode} - {ErrorMessage}", validation.ErrorCode, validation.ErrorMessage);
                    return StatusCode(422, new { success = false, errorCode = validation.ErrorCode, error = validation.ErrorMessage });
                }

                // Step 2: Check warehouse balance
                var balanceFields = new Dictionary<string, string>
                {
                    { "CONO", "300" },
                    { "WHLO", whlo ?? string.Empty },
                    { "ITNO", itno ?? string.Empty }
                };

                var balanceResponse = await _service.ExecuteTransactionAsync("MMS200MI", "GetItmWhsBal", balanceFields) as Dictionary<string, object>;
                if (balanceResponse == null)
                {
                    _logger.LogError("[StockManagement] GetItmWhsBal returned empty response");
                    return StatusCode(502, new { success = false, error = "Unable to retrieve warehouse balance" });
                }

                decimal onHandBalance = balanceResponse.TryGetValue("STQT", out var stqtObj) ? Convert.ToDecimal(stqtObj) : 0;
                decimal newQtyDecimal = decimal.Parse(newQty);

                if (onHandBalance < newQtyDecimal)
                {
                    _logger.LogWarning("[StockManagement] Insufficient stock: On-hand={OnHand}, Requested={Requested}", onHandBalance, newQtyDecimal);
                    return StatusCode(422, new
                    {
                        success = false,
                        errorCode = "004",
                        error = "Insufficient available stock in warehouse",
                        onHandBalance,
                        requestedQuantity = newQtyDecimal
                    });
                }

                // Step 3: Update stock via MMS310MI.Update
                var updateFields = new Dictionary<string, string>
                {
                    { "CONO", "300" },
                    { "WHLO", whlo ?? string.Empty },
                    { "ITNO", itno ?? string.Empty },
                    { "WHSL", whsl ?? string.Empty },
                    { "STQI", newQty ?? string.Empty },
                    { "BREM", brem }
                };

                var updateResponse = await _service.ExecuteTransactionAsync("MMS310MI", "Update", updateFields) as Dictionary<string, object>;

                object updateData = updateResponse ?? (object)new { message = "Stock updated" };

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
                // Extract inputs
                string? itno = request.ContainsKey("ITNO") ? request["ITNO"]?.ToString() : null;
                string? faci = request.ContainsKey("FACI") ? request["FACI"]?.ToString() : null;

                if (string.IsNullOrWhiteSpace(itno) || string.IsNullOrWhiteSpace(faci))
                {
                    return BadRequest(new { success = false, error = "Required fields: ITNO, FACI" });
                }

                _logger.LogInformation("[GetStandardCost] Processing: ITNO={ITNO}, FACI={FACI}", itno, faci);

                // Step 1: Validate item (STAT 20/50, ITTY 030)
                var validation = await _validationService.ValidateItemAsync("300", itno);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("[GetStandardCost] Validation failed: {ErrorCode} - {ErrorMessage}", validation.ErrorCode, validation.ErrorMessage);
                    return StatusCode(422, new { success = false, errorCode = validation.ErrorCode, error = validation.ErrorMessage });
                }

                // Step 2: Get unit cost via MMS003.Get
                var costFields = new Dictionary<string, string>
                {
                    { "CONO", "300" },
                    { "FACI", faci ?? string.Empty },
                    { "ITNO", itno ?? string.Empty }
                };

                var costResponse = await _service.ExecuteTransactionAsync("MMS003", "Get", costFields) as Dictionary<string, object>;
                if (costResponse == null)
                {
                    _logger.LogError("[GetStandardCost] MMS003.Get returned empty response - facility may not exist");
                    return StatusCode(422, new { success = false, errorCode = "011", error = "Facility Does Not Exist" });
                }

                decimal unitCost = costResponse.TryGetValue("UCOS", out var ucosObj) ? Convert.ToDecimal(ucosObj) : 0;

                _logger.LogInformation("[GetStandardCost] Retrieved unit cost {UnitCost} for item {ITNO}", unitCost, itno);

                return Ok(new
                {
                    success = true,
                    program = "MMS003",
                    transaction = "Get",
                    validation = new { stat = validation.Status, itty = validation.ItemType },
                    data = new { unitCostPrice = unitCost, facility = faci, itemNumber = itno },
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

    }
}
