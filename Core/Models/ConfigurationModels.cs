// ConfigurationModels.cs - Data models for external transaction configuration
using System;
using System.Collections.Generic;

namespace Movex.API.Core.Models
{
    /// <summary>
    /// Configuration for a single field (request or response)
    /// </summary>
    public class FieldConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Position { get; set; }
        public int Length { get; set; }
        public string Type { get; set; } = string.Empty; // "numeric", "text", "date", etc.
        public bool Required { get; set; }
        public string Description { get; set; } = string.Empty;
        /// <summary>
        /// Optional absolute start index (1-based) for field positioning in the MI command string.
        /// When provided, uses absolute positioning with gap-filling (fills spaces between fields).
        /// Values are absolute positions from the START of the entire MI command (including 15-char transaction name).
        /// Example: startIndex 16 = position 1 after "TransactionName    " (15 chars).
        /// If omitted, fields are built sequentially in Position order (legacy mode).
        /// </summary>
        public int? StartIndex { get; set; }
    }

    /// <summary>
    /// Configuration for a response field
    /// </summary>
    public class ResponseFieldConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Position { get; set; }
        public int Length { get; set; }
        public string Type { get; set; } = string.Empty; // "numeric", "integer", "decimal", "date", "text", "boolean"
        public string Description { get; set; } = string.Empty;
        // Optional absolute start index (1-based) within the raw response.
        // When provided, the parser will extract the field starting at StartIndex-1 for Length characters,
        // independent of other fields. If omitted, fields are parsed sequentially by Position order.
        public int? StartIndex { get; set; }
    }

    /// <summary>
    /// Definition of a single transaction within a program
    /// </summary>
    public class TransactionDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        // Optional HTTP method for execution ("GET" or "POST"). Defaults to POST when null/empty.
        public string HttpMethod { get; set; } = string.Empty;
        // Indicates whether the response contains multiple records.
        public bool ReturnsList { get; set; }
        public List<FieldConfig> Fields { get; set; } = new();
        public List<ResponseFieldConfig> ResponseFields { get; set; } = new();
    }

    /// <summary>
    /// Configuration for an M3 program (collection of transactions)
    /// </summary>
    public class ProgramConfig
    {
        public string Program { get; set; } = string.Empty;
        public string Library { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<TransactionDefinition> Transactions { get; set; } = new();
    }

    /// <summary>
    /// Result of item validation business logic
    /// </summary>
    public class ItemValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int Status { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string ItemNumber { get; set; } = string.Empty;
    }
}
