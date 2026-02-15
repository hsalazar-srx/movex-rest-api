using System;
using System.Collections.Generic;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Movex.API.Swagger
{
    public class HealthEndpointNoAuthOperationFilter : IOperationFilter
    {
        private const string HealthPath = "health";

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation == null || context == null)
            {
                return;
            }

            var relativePath = context.ApiDescription.RelativePath ?? string.Empty;
            if (IsHealthEndpoint(relativePath))
            {
                operation.Security = new List<OpenApiSecurityRequirement>();
            }
        }

        private static bool IsHealthEndpoint(string relativePath)
        {
            var normalized = relativePath.TrimStart('/');
            return string.Equals(normalized, HealthPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
