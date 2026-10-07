using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Documents the JSON envelope applied to success and error responses at runtime.
/// </summary>
public sealed class ApiSuccessResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var code in new[] { "400", "404", "405", "500" })
            operation.Responses.TryAdd(code, new OpenApiResponse());

        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? []);
        if (attributes.OfType<AuthorizeAttribute>().Any() && !attributes.OfType<AllowAnonymousAttribute>().Any())
            foreach (var code in new[] { "401", "403" })
                operation.Responses.TryAdd(code, new OpenApiResponse());

        foreach (var (statusCode, response) in operation.Responses.ToList())
        {
            if (!int.TryParse(statusCode, out var numericStatusCode) || numericStatusCode < 200 || numericStatusCode is >= 300 and < 400)
                continue;

            var documentedStatusCode = numericStatusCode == StatusCodes.Status204NoContent
                ? StatusCodes.Status200OK.ToString()
                : statusCode;

            if (documentedStatusCode != statusCode)
            {
                operation.Responses.Remove(statusCode);
                operation.Responses[documentedStatusCode] = response;
            }

            var successful = numericStatusCode < 300;
            response.Description = successful
                ? "Successful response using the six-field API envelope."
                : "Error response using the six-field API envelope; error contains ProblemDetails.";
            response.Content ??= new Dictionary<string, OpenApiMediaType>();
            if (!successful)
            {
                var errorSchema = response.Content.Values.FirstOrDefault()?.Schema
                    ?? context.SchemaGenerator.GenerateSchema(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), context.SchemaRepository);
                response.Content.Clear();
                response.Content["application/json"] = new OpenApiMediaType
                {
                    Schema = CreateEnvelopeSchema(null, errorSchema, int.Parse(documentedStatusCode), false)
                };
                continue;
            }

            if (response.Content.Count == 0)
                response.Content["application/json"] = new OpenApiMediaType();
            foreach (var mediaType in response.Content.Values)
                mediaType.Schema = CreateEnvelopeSchema(mediaType.Schema, null, int.Parse(documentedStatusCode), true);
        }
    }

    private static OpenApiSchema CreateEnvelopeSchema(OpenApiSchema? dataSchema, OpenApiSchema? errorSchema, int code, bool successful) => new()
    {
        Type = "object",
        Required = new HashSet<string> { "code", "status", "is_success", "data", "message", "error" },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["code"] = new() { Type = "integer", Format = "int32", Example = new Microsoft.OpenApi.Any.OpenApiInteger(code) },
            ["status"] = new() { Type = "string", Enum = [new Microsoft.OpenApi.Any.OpenApiString(successful ? "success" : "error")] },
            ["is_success"] = new() { Type = "boolean", Example = new Microsoft.OpenApi.Any.OpenApiBoolean(successful) },
            ["data"] = dataSchema ?? new OpenApiSchema { Nullable = true },
            ["message"] = new() { Type = "string", Example = new Microsoft.OpenApi.Any.OpenApiString(successful ? "Request completed successfully." : "Request failed.") },
            ["error"] = errorSchema ?? new OpenApiSchema { Nullable = true }
        }
    };
}
