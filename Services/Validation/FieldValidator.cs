using System;
using System.Collections.Generic;
using System.Linq;
using Movex.API.Core.Constants;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Services.Validation
{
    /// <summary>
    /// Validates M3 transaction field values
    /// </summary>
    public class FieldValidator : IFieldValidator
    {
        /// <summary>
        /// Validate APIPROGRAM format (LIBRARY/PROGRAM)
        /// </summary>
        public void ValidateApiProgram(string apiProgram)
        {
            if (string.IsNullOrWhiteSpace(apiProgram))
            {
                throw new ArgumentException("APIPROGRAM cannot be empty");
            }

            if (!ValidationPatterns.ApiProgramRegex.IsMatch(apiProgram))
            {
                throw new ArgumentException(
                    "Invalid APIPROGRAM format. Expected LIBRARY/PROGRAM with alphanumeric/underscore only.");
            }
        }

        /// <summary>
        /// Validate a single field value against its configuration
        /// </summary>
        public void ValidateField(FieldConfig field, string value)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            // Check required fields
            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Required field '{field.Name}' is missing or empty");
            }

            // Skip validation for empty optional fields
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // Validate field type
            if (field.Type == "numeric")
            {
                if (!ValidationPatterns.NumericRegex.IsMatch(value))
                {
                    throw new ArgumentException(
                        $"Field '{field.Name}' must contain only numeric characters (0-9)");
                }
            }
            else if (field.Type == "text")
            {
                if (!ValidationPatterns.TextRegex.IsMatch(value))
                {
                    throw new ArgumentException(
                        $"Field '{field.Name}' contains invalid characters. Allowed: A-Z, a-z, 0-9, space, _, -, ., /, :");
                }
            }

            // Validate field length
            if (value.Length > field.Length)
            {
                throw new ArgumentException(
                    $"Field '{field.Name}' exceeds maximum length of {field.Length} characters (provided: {value.Length})");
            }
        }

        /// <summary>
        /// Validate all required fields are present
        /// </summary>
        public void ValidateRequiredFields(List<FieldConfig> fields, Dictionary<string, string> fieldValues)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            if (fieldValues == null)
            {
                throw new ArgumentNullException(nameof(fieldValues));
            }

            var requiredFields = fields.Where(f => f.Required).ToList();
            var missingFields = new List<string>();

            foreach (var field in requiredFields)
            {
                bool hasValue = fieldValues.ContainsKey(field.Name) && 
                                !string.IsNullOrWhiteSpace(fieldValues[field.Name]);
                
                bool hasPositionValue = fieldValues.ContainsKey(field.Position.ToString()) && 
                                        !string.IsNullOrWhiteSpace(fieldValues[field.Position.ToString()]);

                if (!hasValue && !hasPositionValue)
                {
                    missingFields.Add(field.Name);
                }
            }

            if (missingFields.Any())
            {
                throw new ArgumentException(
                    $"Missing required fields: {string.Join(", ", missingFields)}");
            }
        }

        /// <summary>
        /// Validate no unknown fields are provided
        /// </summary>
        public void ValidateNoUnknownFields(List<FieldConfig> fields, Dictionary<string, string> fieldValues)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            if (fieldValues == null)
            {
                throw new ArgumentNullException(nameof(fieldValues));
            }

            var allowedKeys = new HashSet<string>(
                fields.Select(f => f.Name), 
                StringComparer.OrdinalIgnoreCase);

            var allowedPositions = new HashSet<string>(
                fields.Select(f => f.Position.ToString()), 
                StringComparer.OrdinalIgnoreCase);

            var unknownFields = new List<string>();

            foreach (var key in fieldValues.Keys)
            {
                // APIPROGRAM is a special metadata field, always allowed
                if (string.Equals(key, "APIPROGRAM", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!allowedKeys.Contains(key) && !allowedPositions.Contains(key))
                {
                    unknownFields.Add(key);
                }
            }

            if (unknownFields.Any())
            {
                throw new ArgumentException(
                    $"Unknown fields provided: {string.Join(", ", unknownFields)}");
            }
        }
    }
}
