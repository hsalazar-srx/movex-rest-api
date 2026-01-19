using System;
using System.Collections.Generic;
using System.Linq;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Infrastructure.TransactionBuilding
{
    /// <summary>
    /// Builds M3 transaction strings from configuration and field values
    /// Supports both sequential positioning (legacy) and absolute positioning (modern)
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
        /// Automatically detects positioning mode based on field definitions
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

            // Determine positioning mode based on field definitions
            bool useAbsolutePositioning = definition.Fields.Any(f => f.StartIndex.HasValue && f.StartIndex.Value > 0);

            string fieldsString;
            if (useAbsolutePositioning)
            {
                // Modern: Absolute positioning with gaps (for transactions like MMS310MI)
                fieldsString = BuildFieldsWithAbsolutePositioning(definition.Fields, fieldValues);
            }
            else
            {
                // Legacy: Sequential positioning (for existing transactions)
                fieldsString = BuildFieldsWithSequentialPositioning(definition.Fields, fieldValues);
            }

            return paddedTransaction + fieldsString;
        }

        /// <summary>
        /// Build fields using absolute positioning (StartIndex) - fills gaps with spaces
        /// Used for modern transaction definitions with explicit absolute positions
        /// StartIndex values are absolute positions from the start of the entire MI command
        /// We subtract <c>TransactionNameLength</c> to convert absolute positions (which include the 15-character transaction name prefix) into buffer-relative indices.
        /// </summary>
        private string BuildFieldsWithAbsolutePositioning(List<FieldConfig> fields, Dictionary<string, string> fieldValues)
        {
            const int TransactionNameLength = 16; // Offset from start of MI command to first field (accounts for the 15-character transaction name prefix)

            // Calculate total buffer size needed (accounting for transaction name offset)
            int maxPosition = 0;
            foreach (var field in fields)
            {
                if (field.StartIndex.HasValue && field.StartIndex.Value > 0)
                {
                    // Adjust for transaction name offset
                    int adjustedStartIndex = field.StartIndex.Value - TransactionNameLength;
                    int fieldEndPosition = adjustedStartIndex + field.Length;
                    maxPosition = Math.Max(maxPosition, fieldEndPosition);
                }
            }

            // Create buffer filled with spaces
            char[] buffer = new char[maxPosition];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = ' ';
            }

            // Place each field at its absolute position (adjusted for transaction name)
            var sortedFields = fields.OrderBy(f => f.StartIndex ?? 0).ToList();

            foreach (var field in sortedFields)
            {
                if (!field.StartIndex.HasValue || field.StartIndex.Value <= 0)
                {
                    // Skip fields without valid StartIndex in absolute positioning mode
                    continue;
                }

                string value = ResolveFieldValue(field, fieldValues);
                
                // Validate the field
                _validator.ValidateField(field, value);

                // Format the field value
                string paddedValue = PadField(field, value);

                // Place field in buffer at absolute position, adjusted for 15-char transaction name
                int startPos = field.StartIndex.Value - TransactionNameLength;
                for (int i = 0; i < paddedValue.Length && startPos + i < buffer.Length; i++)
                {
                    buffer[startPos + i] = paddedValue[i];
                }
            }

            return new string(buffer);
        }

        /// <summary>
        /// Build fields using sequential positioning (Position) - legacy mode
        /// Fields are concatenated in order without gaps
        /// </summary>
        private string BuildFieldsWithSequentialPositioning(List<FieldConfig> fields, Dictionary<string, string> fieldValues)
        {
            var sortedFields = fields.OrderBy(f => f.Position).ToList();
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

            return fieldsString;
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
