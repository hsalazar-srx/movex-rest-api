using System.Threading.Tasks;
using Movex.API.Core.Models;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for validating M3 items according to business rules
    /// </summary>
    public interface IItemValidationService
    {
        /// <summary>
        /// Validate item status and type via MMS200MI.GetItmBasic
        /// </summary>
        /// <param name="company">Company number (CONO)</param>
        /// <param name="itemNumber">Item number (ITNO)</param>
        /// <returns>Validation result with status, type, and any error codes</returns>
        Task<ItemValidationResult> ValidateItemAsync(string company, string itemNumber);
    }
}
