using System.Collections.Generic;
using Movex.API.Core.Models;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for validating M3 transaction field values
    /// </summary>
    public interface IFieldValidator
    {
        /// <summary>
        /// Validate APIPROGRAM format (LIBRARY/PROGRAM)
        /// </summary>
        void ValidateApiProgram(string apiProgram);

        /// <summary>
        /// Validate a single field value against its configuration
        /// </summary>
        void ValidateField(FieldConfig field, string value);

        /// <summary>
        /// Validate all required fields are present
        /// </summary>
        void ValidateRequiredFields(List<FieldConfig> fields, Dictionary<string, string> fieldValues);

        /// <summary>
        /// Validate no unknown fields are provided
        /// </summary>
        void ValidateNoUnknownFields(List<FieldConfig> fields, Dictionary<string, string> fieldValues);
    }
}
