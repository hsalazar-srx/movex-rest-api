using System.Collections.Generic;
using Movex.API.Core.Models;

namespace Movex.API.Core.Interfaces
{
    /// <summary>
    /// Service for parsing M3 response strings into structured data
    /// </summary>
    public interface IResponseParser
    {
        /// <summary>
        /// Parse raw M3 response string into structured dictionary
        /// </summary>
        /// <param name="rawResponse">Raw response from M3</param>
        /// <param name="responseFields">Response field configurations</param>
        /// <returns>Parsed response as dictionary</returns>
        Dictionary<string, object?> ParseResponse(string rawResponse, List<ResponseFieldConfig> responseFields);

        /// <summary>
        /// Parse raw M3 response string into a list of records when the transaction returns multiple entries
        /// </summary>
        /// <param name="rawResponse">Raw response from M3 (may contain multiple records)</param>
        /// <param name="responseFields">Response field configurations</param>
        /// <returns>List of parsed records</returns>
        List<Dictionary<string, object?>> ParseResponseList(string rawResponse, List<ResponseFieldConfig> responseFields);
    }
}
