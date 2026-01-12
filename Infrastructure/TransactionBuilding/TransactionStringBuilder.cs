using System;
using System.Collections.Generic;
using System.Linq;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Infrastructure.TransactionBuilding
{
    /// <summary>
    /// Builds M3 transaction strings from configuration and field values
    /// </summary>
    public class TransactionStringBuilder : ITransactionStringBuilder
    {
        private readonly IFieldValidator _validator;

        public TransactionStringBuilder(IFieldValidator validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// <summary>
        /// Build a transaction string from transaction definition and field values
        /// </summary>
        public string BuildTransactionString(TransactionDefinition definition, Dictionary<string, string> fieldValues)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (fieldValues == null)
            {
                throw new ArgumentNullException(nameof(fieldValues));
            }

            // Validate APIPROGRAM if provided
            if (fieldValues.TryGetValue("APIPROGRAM", out var apiProgram) && 
                !string.IsNullOrWhiteSpace(apiProgram))
            {
                _validator.ValidateApiProgram(apiProgram);
            }

            // Validate no unknown fields
            _validator.ValidateNoUnknownFields(definition.Fields, fieldValues);

            // Validate required fields
            _validator.ValidateRequiredFields(definition.Fields, fieldValues);

            // Pad transaction name to 15 characters
            string paddedTransaction = definition.Name.PadRight(15, ' ');

            // Build fields in order
            var sortedFields = definition.Fields.OrderBy(f => f.Position).ToList();
            string fieldsString = "";

            foreach (var field in sortedFields)
            {
                string value = ResolveFieldValue(field, fieldValues);
                
                // Validate the field
                _validator.ValidateField(field, value);

                // Pad the field value
                string paddedValue = PadField(field, value);
                
                fieldsString += paddedValue;
            }

            return paddedTransaction + fieldsString;
        }

        /// <summary>
        /// Resolve field value from fieldValues dictionary (supports both name and position lookup)
        /// </summary>
        private string ResolveFieldValue(FieldConfig field, Dictionary<string, string> fieldValues)
        {
            string value = "";

            // Try to get value by field name first
            if (fieldValues.ContainsKey(field.Name))
            {
                value = fieldValues[field.Name];
            }
            // Fallback to position-based lookup
            else if (fieldValues.ContainsKey(field.Position.ToString()))
            {
                value = fieldValues[field.Position.ToString()];
            }

            return value ?? "";
        }

        /// <summary>
        /// Pad field value according to type and length
        /// </summary>
        private string PadField(FieldConfig field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = "";
            }

            // Numeric fields: right-align with leading zeros
            if (field.Type == "numeric")
            {
                return value.PadLeft(field.Length, '0');
            }
            // Text fields: left-align with trailing spaces
            else
            {
                return value.PadRight(field.Length, ' ');
            }
        }
    }
}
