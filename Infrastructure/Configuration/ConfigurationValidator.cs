// ConfigurationValidator.cs - Validate transaction configs against JSON schema
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Movex.API.Infrastructure.Configuration
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class ConfigurationValidator
    {
        private readonly string _schemaPath;

        public ConfigurationValidator(string configPath)
        {
            _schemaPath = Path.Combine(configPath, "schema.json");
        }

        /// <summary>
        /// Validate a JSON configuration against the schema
        /// Note: This is a simplified validation that checks required fields and basic structure.
        /// For production, consider using a full JSON Schema validator (e.g., NJsonSchema).
        /// </summary>
        public ValidationResult Validate(string json)
        {
            var result = new ValidationResult { IsValid = true };

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check required fields
                if (!root.TryGetProperty("program", out var programProp) || string.IsNullOrWhiteSpace(programProp.GetString()))
                {
                    result.Errors.Add("Required field 'program' is missing or empty");
                    result.IsValid = false;
                }

                if (!root.TryGetProperty("transactions", out var transactionsProp) || transactionsProp.ValueKind != JsonValueKind.Array)
                {
                    result.Errors.Add("Required field 'transactions' must be an array");
                    result.IsValid = false;
                    return result;
                }

                var transactions = transactionsProp.EnumerateArray().ToList();
                if (transactions.Count == 0)
                {
                    result.Errors.Add("'transactions' array must contain at least one transaction");
                    result.IsValid = false;
                }

                // Validate each transaction
                int txIndex = 0;
                foreach (var tx in transactions)
                {
                    var txErrors = ValidateTransaction(tx, txIndex);
                    result.Errors.AddRange(txErrors);
                    if (txErrors.Any())
                        result.IsValid = false;
                    txIndex++;
                }
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Invalid JSON: {ex.Message}");
            }

            return result;
        }

        private List<string> ValidateTransaction(JsonElement transaction, int index)
        {
            var errors = new List<string>();
            var prefix = $"Transaction[{index}]";

            if (!transaction.TryGetProperty("name", out var nameProp) || string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                errors.Add($"{prefix}: Required field 'name' is missing or empty");
            }

            if (!transaction.TryGetProperty("fields", out var fieldsProp) || fieldsProp.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{prefix}: Required field 'fields' must be an array");
            }
            else
            {
                var fields = fieldsProp.EnumerateArray().ToList();
                if (fields.Count == 0)
                {
                    errors.Add($"{prefix}: 'fields' array must contain at least one field");
                }

                // Validate each field
                int fieldIndex = 0;
                foreach (var field in fields)
                {
                    var fieldErrors = ValidateField(field, fieldIndex, prefix);
                    errors.AddRange(fieldErrors);
                    fieldIndex++;
                }
            }

            // Validate response fields if present
            if (transaction.TryGetProperty("responseFields", out var responseProp) && responseProp.ValueKind == JsonValueKind.Array)
            {
                var responses = responseProp.EnumerateArray().ToList();
                int respIndex = 0;
                foreach (var resp in responses)
                {
                    var respErrors = ValidateField(resp, respIndex, $"{prefix}.responseFields");
                    errors.AddRange(respErrors);
                    respIndex++;
                }
            }

            return errors;
        }

        private List<string> ValidateField(JsonElement field, int index, string prefix)
        {
            var errors = new List<string>();
            var fieldPrefix = $"{prefix}[{index}]";

            if (!field.TryGetProperty("name", out var nameProp) || string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                errors.Add($"{fieldPrefix}: Required field 'name' is missing or empty");
            }

            if (!field.TryGetProperty("position", out var posProp) || posProp.GetInt32() < 1)
            {
                errors.Add($"{fieldPrefix}: Field 'position' must be >= 1");
            }

            if (!field.TryGetProperty("length", out var lenProp) || lenProp.GetInt32() < 1)
            {
                errors.Add($"{fieldPrefix}: Field 'length' must be >= 1");
            }

            if (!field.TryGetProperty("type", out var typeProp) || string.IsNullOrWhiteSpace(typeProp.GetString()))
            {
                errors.Add($"{fieldPrefix}: Required field 'type' is missing or empty");
            }
            else
            {
                var typeValue = typeProp.GetString()?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(typeValue))
                {
                    errors.Add($"{fieldPrefix}: Required field 'type' is missing or empty");
                    return errors;
                }
                var validTypes = new[] { "numeric", "text", "integer", "decimal", "date", "boolean" };
                if (!validTypes.Contains(typeValue))
                {
                    errors.Add($"{fieldPrefix}: Field 'type' must be one of: {string.Join(", ", validTypes)}");
                }
            }

            return errors;
        }
    }
}
