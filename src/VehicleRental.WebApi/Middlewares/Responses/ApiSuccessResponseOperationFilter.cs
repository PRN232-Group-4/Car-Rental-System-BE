using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Documents the JSON envelope applied to success and error responses at runtime, and applies the wording each
/// action declares for itself through <see cref="OperationDescriptionAttribute"/>,
/// <see cref="OperationSummaryAttribute"/>, <see cref="BinaryAttachmentResponseAttribute"/> and the parameter
/// documentation attributes.
/// </summary>
/// <remarks>
/// Nothing here compares controller or action names: the per-endpoint text travels with the action, so moving
/// an action to another controller cannot drop or duplicate its documentation.
/// </remarks>
public sealed class ApiSuccessResponseOperationFilter : IOperationFilter
{
    /// <summary>Appended to every operation: the naming convention of the whole API.</summary>
    private const string RouteNamingNote =
        " Route naming uses plural collection resources and singular resources addressed by ID; creating an item targets the plural collection. Singleton resources and action names keep their own names.";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var methodInfo = context.MethodInfo;

        foreach (var declared in methodInfo.GetCustomAttributes<OperationDescriptionAttribute>(inherit: true))
        {
            operation.Description = (operation.Description ?? "") + declared.Text;
            if (declared.DocumentsPaginationParameters)
                DocumentPaginationParameters(operation);
        }

        if (methodInfo.GetCustomAttributes<OperationSummaryAttribute>(inherit: true).FirstOrDefault() is { } summary)
            operation.Summary = summary.Text;

        foreach (var documented in methodInfo.GetCustomAttributes<QueryParameterEnumAttribute>(inherit: true))
        {
            var parameter = operation.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, documented.ParameterName, StringComparison.OrdinalIgnoreCase));
            if (parameter is not null)
                parameter.Schema.Enum = documented.Values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }

        foreach (var bound in methodInfo.GetCustomAttributes<SchemaPropertyMinimumAttribute>(inherit: true))
        {
            if (context.SchemaRepository.Schemas.TryGetValue(bound.SchemaId, out var schema) &&
                schema.Properties.TryGetValue(bound.PropertyName, out var property))
            {
                property.Minimum = bound.Minimum;
            }
        }

        operation.Description = (operation.Description ?? "") + RouteNamingNote;

        foreach (var code in new[] { "400", "404", "405", "500" })
            operation.Responses.TryAdd(code, new OpenApiResponse());

        var attributes = methodInfo.GetCustomAttributes(true)
            .Concat(methodInfo.DeclaringType?.GetCustomAttributes(true) ?? []);
        if (attributes.OfType<Microsoft.AspNetCore.Mvc.ConsumesAttribute>().Any())
            operation.Responses.TryAdd("415", new OpenApiResponse());
        if (attributes.OfType<AuthorizeAttribute>().Any() && !attributes.OfType<AllowAnonymousAttribute>().Any())
            foreach (var code in new[] { "401", "403" })
                operation.Responses.TryAdd(code, new OpenApiResponse());

        var binaryAttachment = methodInfo.GetCustomAttributes<BinaryAttachmentResponseAttribute>(inherit: true).FirstOrDefault();
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
            if (successful && binaryAttachment is not null)
            {
                response.Description = binaryAttachment.Description;
                response.Content.Clear();
                foreach (var contentType in binaryAttachment.ContentTypes)
                    response.Content[contentType] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = "string", Format = "binary" } };
                continue;
            }
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

    /// <summary>Documents the minimum, default and maximum of the pagination parameters of one operation.</summary>
    private static void DocumentPaginationParameters(OpenApiOperation operation)
    {
        foreach (var parameter in operation.Parameters.Where(p =>
                     p.Name.Equals("page", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase)))
        {
            var size = parameter.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase);
            parameter.Schema.Minimum = 1;
            parameter.Schema.Default = new OpenApiInteger(size ? 20 : 1);
            if (size) parameter.Schema.Maximum = 100;
        }
    }

    private static OpenApiSchema CreateEnvelopeSchema(OpenApiSchema? dataSchema, OpenApiSchema? errorSchema, int code, bool successful) => new()
    {
        Type = "object",
        Required = new HashSet<string> { "code", "status", "is_success", "data", "message", "error" },
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["code"] = new() { Type = "integer", Format = "int32", Example = new OpenApiInteger(code) },
            ["status"] = new() { Type = "string", Enum = [new OpenApiString(successful ? "success" : "error")] },
            ["is_success"] = new() { Type = "boolean", Example = new OpenApiBoolean(successful) },
            ["data"] = dataSchema ?? new OpenApiSchema { Nullable = true },
            ["message"] = new() { Type = "string", Example = new OpenApiString(successful ? "Request completed successfully." : "Request failed.") },
            ["error"] = errorSchema ?? new OpenApiSchema { Nullable = true }
        }
    };
}
