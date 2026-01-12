using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Services
{
    /// <summary>
    /// Service for validating M3 items according to MES business rules
    /// </summary>
    public class ItemValidationService : IItemValidationService
    {
        private readonly ITransactionService _transactionService;
        private readonly ILogger<ItemValidationService> _logger;

        public ItemValidationService(
            ITransactionService transactionService,
            ILogger<ItemValidationService> logger)
        {
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validate item status and type via MMS200MI.GetItmBasic
        /// </summary>
        public async Task<ItemValidationResult> ValidateItemAsync(string company, string itemNumber)
        {
            try
            {
                var companyValue = company ?? string.Empty;
                var itemNumberValue = itemNumber ?? string.Empty;

                _logger.LogInformation("Validating item {ItemNumber} for company {Company}", itemNumberValue, companyValue);

                // Call MMS200MI.GetItmBasic to get item details
                var fields = new Dictionary<string, string>
                {
                    { "CONO", companyValue },
                    { "ITNO", itemNumberValue }
                };

                var response = await _transactionService.ExecuteTransactionAsync("MMS200MI", "GetItmBasic", fields) as Dictionary<string, object>;

                if (response == null || response.Count == 0)
                {
                    _logger.LogWarning("Item {ItemNumber} not found in company {Company}", itemNumberValue, companyValue);
                    return new ItemValidationResult
                    {
                        IsValid = false,
                        ErrorCode = "001",
                        ErrorMessage = "Item Does Not Exist",
                        ItemNumber = itemNumberValue
                    };
                }

                // Extract status and item type
                int stat = response.TryGetValue("STAT", out var statObj) ? Convert.ToInt32(statObj) : 0;
                string itty = response.TryGetValue("ITTY", out var ittyObj) ? ittyObj?.ToString()?.Trim() ?? string.Empty : string.Empty;

                _logger.LogDebug("Item {ItemNumber}: STAT={Status}, ITTY={ItemType}", itemNumber, stat, itty);

                // Validate status: must be 20 (Active) or 50 (Released)
                if (stat != 20 && stat != 50)
                {
                    _logger.LogWarning("Item {ItemNumber} has invalid status {Status}", itemNumberValue, stat);
                    return new ItemValidationResult
                    {
                        IsValid = false,
                        ErrorCode = "002",
                        ErrorMessage = "Item is not active/released",
                        Status = stat,
                        ItemType = itty,
                        ItemNumber = itemNumberValue
                    };
                }

                // Validate item type: must be 030 (raw material)
                if (!string.Equals(itty, "030", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Item {ItemNumber} has invalid type {ItemType}", itemNumberValue, itty);
                    return new ItemValidationResult
                    {
                        IsValid = false,
                        ErrorCode = "003",
                        ErrorMessage = "Item is not a raw material",
                        Status = stat,
                        ItemType = itty,
                        ItemNumber = itemNumberValue
                    };
                }

                _logger.LogInformation("Item {ItemNumber} validation successful", itemNumberValue);
                return new ItemValidationResult
                {
                    IsValid = true,
                    Status = stat,
                    ItemType = itty,
                    ItemNumber = itemNumberValue
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating item {ItemNumber}: {Message}", itemNumber, ex.Message);
                throw;
            }
        }
    }
}
