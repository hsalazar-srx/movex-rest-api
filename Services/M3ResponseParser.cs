// M3ResponseParser.cs - Parse raw M3 response strings into structured JSON
using System;
using System.Collections.Generic;
using System.Linq;
using Movex.API.Core.Interfaces;
using Movex.API.Core.Models;

namespace Movex.API.Services
{
    public class M3ResponseParser : IResponseParser
    {
        /// <summary>
        /// Parse raw M3 response string into a dictionary of field values
        /// </summary>
        public Dictionary<string, object?> ParseResponse(string rawResponse, List<ResponseFieldConfig> fieldConfigs)
        {
            var result = new Dictionary<string, object?>();

            if (string.IsNullOrEmpty(rawResponse) || fieldConfigs == null || !fieldConfigs.Any())
            {
                return result;
            }

            // Determine if any fields specify absolute StartIndex
            bool hasAbsolute = fieldConfigs.Any(f => f.StartIndex.GetValueOrDefault() > 0);

            if (hasAbsolute)
            {
                // Parse absolute-indexed fields independently
                foreach (var field in fieldConfigs.Where(f => f.StartIndex.GetValueOrDefault() > 0)
                                                  .OrderBy(f => f.StartIndex.GetValueOrDefault()))
                {
                    try
                    {
                        int start = Math.Max(0, field.StartIndex.GetValueOrDefault() - 1);
                        if (start >= rawResponse.Length)
                        {
                            Console.WriteLine($"[M3ResponseParser] Warning: StartIndex {field.StartIndex} exceeds response length {rawResponse.Length} for field '{field.Name}'");
                            result[field.Name] = null;
                            continue;
                        }

                        int end = Math.Min(start + field.Length, rawResponse.Length);
                        string rawValue = rawResponse.Substring(start, end - start).Trim();
                        object? convertedValue = ConvertValue(rawValue, field.Type, field.Name);
                        result[field.Name] = convertedValue;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[M3ResponseParser] Error parsing field '{field.Name}' at StartIndex {field.StartIndex}: {ex.Message}");
                        result[field.Name] = null;
                    }
                }

                // For any remaining fields without StartIndex, fall back to sequential parsing from the start
                var remaining = fieldConfigs.Where(f => !f.StartIndex.HasValue || f.StartIndex.GetValueOrDefault() <= 0)
                                            .OrderBy(f => f.Position)
                                            .ToList();
                int currentPosition = 0;
                foreach (var field in remaining)
                {
                    try
                    {
                        if (currentPosition >= rawResponse.Length)
                        {
                            Console.WriteLine($"[M3ResponseParser] Warning: Position {currentPosition} exceeds response length {rawResponse.Length} for field '{field.Name}'");
                            result[field.Name] = null;
                            break;
                        }

                        int endPosition = Math.Min(currentPosition + field.Length, rawResponse.Length);
                        string rawValue = rawResponse.Substring(currentPosition, endPosition - currentPosition).Trim();
                        object? convertedValue = ConvertValue(rawValue, field.Type, field.Name);
                        result[field.Name] = convertedValue;
                        currentPosition += field.Length;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[M3ResponseParser] Error parsing field '{field.Name}' at position {currentPosition}: {ex.Message}");
                        result[field.Name] = null;
                    }
                }

                return result;
            }
            else
            {
                // Sequential parsing using Position order
                var sortedFields = fieldConfigs.OrderBy(f => f.Position).ToList();
                int currentPosition = 0;

                foreach (var field in sortedFields)
                {
                    try
                    {
                        // Check bounds
                        if (currentPosition >= rawResponse.Length)
                        {
                            Console.WriteLine($"[M3ResponseParser] Warning: Position {currentPosition} exceeds response length {rawResponse.Length} for field '{field.Name}'");
                            break;
                        }

                        // Extract field value
                        int endPosition = Math.Min(currentPosition + field.Length, rawResponse.Length);
                        string rawValue = rawResponse.Substring(currentPosition, endPosition - currentPosition).Trim();

                        // Convert based on type
                        object? convertedValue = ConvertValue(rawValue, field.Type, field.Name);
                        result[field.Name] = convertedValue;

                        currentPosition += field.Length;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[M3ResponseParser] Error parsing field '{field.Name}' at position {currentPosition}: {ex.Message}");
                        result[field.Name] = null;
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Parse a multi-record response. If the raw response contains newline delimiters, split by lines.
        /// Otherwise, split by fixed record length (sum of field lengths).
        /// </summary>
        public List<Dictionary<string, object?>> ParseResponseList(string rawResponse, List<ResponseFieldConfig> fieldConfigs)
        {
            var records = new List<Dictionary<string, object?>>();

            if (string.IsNullOrEmpty(rawResponse) || fieldConfigs == null || !fieldConfigs.Any())
            {
                return records;
            }

            // Determine delimiter or fixed record length
            var lineSeparators = new[] { "\r\n", "\n" };
            var containsNewlines = lineSeparators.Any(sep => rawResponse.Contains(sep));

            if (containsNewlines)
            {
                var lines = rawResponse.Split(lineSeparators, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parsed = ParseResponse(line, fieldConfigs);
                    if (parsed.Count > 0)
                    {
                        records.Add(parsed);
                    }
                }
                return records;
            }

            // Fallback: fixed-length records based on total field length
            int recordLength = fieldConfigs.Sum(f => Math.Max(f.Length, 0));
            if (recordLength <= 0)
            {
                // Cannot determine how to split; return a single parsed record
                var single = ParseResponse(rawResponse, fieldConfigs);
                if (single.Count > 0)
                {
                    records.Add(single);
                }
                return records;
            }

            for (int offset = 0; offset < rawResponse.Length; offset += recordLength)
            {
                int remaining = Math.Min(recordLength, rawResponse.Length - offset);
                if (remaining <= 0) break;
                string chunk = rawResponse.Substring(offset, remaining);
                var parsed = ParseResponse(chunk, fieldConfigs);
                if (parsed.Count > 0)
                {
                    records.Add(parsed);
                }
            }

            return records;
        }

        /// <summary>
        /// Convert a string value to the appropriate type
        /// </summary>
        private object? ConvertValue(string value, string type, string fieldName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return type.ToLower() switch
                {
                    "numeric" or "decimal" => 0m,
                    "integer" or "int" => 0,
                    "boolean" or "bool" => false,
                    _ => string.Empty
                };
            }

            return type.ToLower() switch
            {
                "numeric" => ConvertToDecimal(value, fieldName),
                "decimal" => ConvertToDecimal(value, fieldName),
                "integer" or "int" => ConvertToInteger(value, fieldName),
                "date" => ParseM3Date(value),
                "boolean" or "bool" => ParseBoolean(value),
                "text" or _ => value
            };
        }

        private decimal ConvertToDecimal(string value, string fieldName)
        {
            if (decimal.TryParse(value, out var result))
                return result;

            Console.WriteLine($"[M3ResponseParser] Warning: Could not parse '{value}' as decimal for field '{fieldName}', using 0");
            return 0;
        }

        private int ConvertToInteger(string value, string fieldName)
        {
            if (int.TryParse(value, out var result))
                return result;

            Console.WriteLine($"[M3ResponseParser] Warning: Could not parse '{value}' as integer for field '{fieldName}', using 0");
            return 0;
        }

        /// <summary>
        /// Parse M3 date format (YYMMDD or YYYYMMDD)
        /// </summary>
        private string? ParseM3Date(string value)
        {
            if (string.IsNullOrEmpty(value) || value.All(c => c == '0'))
            return null; // Zero date = null

            try
            {
                // Try parsing as YYMMDD (6 chars) or YYYYMMDD (8 chars)
                if (value.Length == 6 && int.TryParse(value, out var dateVal6))
                {
                    int year = 2000 + int.Parse(value.Substring(0, 2));
                    int month = int.Parse(value.Substring(2, 2));
                    int day = int.Parse(value.Substring(4, 2));
                    return new DateTime(year, month, day).ToString("yyyy-MM-dd");
                }
                else if (value.Length == 8 && int.TryParse(value, out var dateVal8))
                {
                    int year = int.Parse(value.Substring(0, 4));
                    int month = int.Parse(value.Substring(4, 2));
                    int day = int.Parse(value.Substring(6, 2));
                    return new DateTime(year, month, day).ToString("yyyy-MM-dd");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[M3ResponseParser] Warning: Could not parse date '{value}': {ex.Message}");
            }

            return value; // Return as-is if parsing fails
        }

        private bool ParseBoolean(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
