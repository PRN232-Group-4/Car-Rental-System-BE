using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Car_Rental_System_BE.Responses;

/// <summary>
/// Makes Swagger describe the success-response envelope applied at runtime.
/// </summary>
public sealed class ApiSuccessResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var (statusCode, response) in operation.Responses.ToList())
        {
            if (!int.TryParse(statusCode, out var numericStatusCode) || numericStatusCode is < 200 or >= 300)
                continue;

            var documentedStatusCode = numericStatusCode == StatusCodes.Status204NoContent
                ? StatusCodes.Status200OK.ToString()
                : statusCode;

            if (documentedStatusCode != statusCode)
            {
                operation.Responses.Remove(statusCode);
                operation.Responses[documentedStatusCode] = response;
            }

            response.Description = "Successful response. The body uses the standard API response envelope.";
            response.Content ??= new Dictionary<string, OpenApiMediaType>();

            if (response.Content.Count == 0)
            {
                response.Content["application/json"] = new OpenApiMediaType
                {
                    Schema = CreateEnvelopeSchema(null)
                };
                continue;
            }

            foreach (var mediaType in response.Content.Values)
                mediaType.Schema = CreateEnvelopeSchema(mediaType.Schema);
        }
    }

    private static OpenApiSchema CreateEnvelopeSchema(OpenApiSchema? dataSchema) => new()
    {
        Type = "object",
        Required = new HashSet<string> { "message", "is_success", "data" },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["message"] = new() { Type = "string", Example = new Microsoft.OpenApi.Any.OpenApiString("Vehicle created successfully.") },
            ["is_success"] = new() { Type = "boolean", Example = new Microsoft.OpenApi.Any.OpenApiBoolean(true) },
            ["data"] = dataSchema ?? new OpenApiSchema { Nullable = true }
        }
    };
}
