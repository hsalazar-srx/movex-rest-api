using System.Text.RegularExpressions;

namespace Movex.API.Core.Constants
{
    /// <summary>
    /// Validation regex patterns for M3 transaction fields
    /// </summary>
    public static class ValidationPatterns
    {
        /// <summary>
        /// Pattern for numeric fields (0-9 only)
        /// </summary>
        public static readonly Regex NumericRegex = new(@"^[0-9]*$", RegexOptions.Compiled);

        /// <summary>
        /// Pattern for text fields (alphanumeric, space, and common symbols)
        /// </summary>
        public static readonly Regex TextRegex = new(@"^[A-Za-z0-9 _\-\./:]*$", RegexOptions.Compiled);

        /// <summary>
        /// Pattern for APIPROGRAM format (LIBRARY/PROGRAM)
        /// </summary>
        public static readonly Regex ApiProgramRegex = new(@"^[A-Za-z0-9_]+/[A-Za-z0-9_]+$", RegexOptions.Compiled);
    }
}
