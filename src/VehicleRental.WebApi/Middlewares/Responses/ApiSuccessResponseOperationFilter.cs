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
        var controller = context.MethodInfo.DeclaringType?.Name;
        var action = context.MethodInfo.Name;
        if ((controller == "RentalQueriesController" && action is "List" or "Mine") ||
            (controller == "HandoversController" && action == "List"))
        {
            operation.Description = "Pagination defaults to page=1/pageSize=20; page>=1, pageSize 1..100. Invalid pagination returns 400. data contains items/page/pageSize/totalCount with the count after filtering and before pagination. Results are ordered by rental ID descending; out-of-range pages return empty items.";
            foreach (var parameter in operation.Parameters.Where(p => p.In == ParameterLocation.Query))
            {
                if (parameter.Name == "page")
                {
                    parameter.Schema.Minimum = 1;
                    parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(1);
                }
                if (parameter.Name == "pageSize")
                {
                    parameter.Schema.Minimum = 1;
                    parameter.Schema.Maximum = 100;
                    parameter.Schema.Default = new Microsoft.OpenApi.Any.OpenApiInteger(20);
                }
            }
            if (controller == "HandoversController")
                operation.Description += " Admin/Manager/CustomerService only. status=pending (default), completed or all. Completed handovers include rentals that have since been returned/completed.";
        }
        if (controller == "RentalEditingController")
            operation.Description = "Admin/Manager/CustomerService only. Updates vehicleId/startTime/endTime/hasDriver before rental start (pending or pre_inspection, started_at null). Times must be UTC. Immutable customer/type/subscription/policy and server-computed prices cannot be supplied. Conflicting bookings, existing inspection/assignment, signed contracts and committed payments block changes with 409. Unpaid invoice and booking are updated in the same transaction.";
        if (controller == "HandoversController" && action == "Confirm")
            operation.Description = "Admin/Manager/CustomerService only. Confirms successful delivery using the rental ID, with no body. Requires pre_inspection, a passing inspection for the current vehicle, valid assignment, and an approved non-expired license for self-drive. Atomically starts the rental (renting) and records history; repeated confirmation returns 409.";
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "Policies")
        {
            operation.Summary = "List rental policy versions with rental usage counts (Admin/Manager).";
            operation.Description = "Pagination defaults to page=1/pageSize=20 (page>=1, pageSize 1..100); data contains items/page/pageSize/totalCount. rentalCount counts all rentals referencing that exact policy, including cancelled rentals; results are ordered by ID descending.";
        }
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "AddPolicy")
        {
            operation.Description = "Creates a policy only when its vehicleTypeId exists (null means global) and no active policy exists in the same scope. Validation failures use error.errors.VehicleTypeId. New policies have rentalCount=0.";
            if (context.SchemaRepository.Schemas.TryGetValue("PolicyRequest", out var request) && request.Properties.TryGetValue("vehicleTypeId", out var typeId)) typeId.Minimum = 1;
        }
        if (context.MethodInfo.DeclaringType?.Name == "OperationsController" && context.MethodInfo.Name == "SetPolicyStatus")
        {
            operation.Summary = "Set rental policy active status (Admin/Manager).";
            operation.Description = "Sets only isActive; policy terms and existing rental snapshots remain unchanged. Repeating the same status is idempotent. Activation is rejected if another active policy exists in the same scope. rentalCount is preserved in the response.";
        }
        foreach (var code in new[] { "400", "404", "405", "500" })
            operation.Responses.TryAdd(code, new OpenApiResponse());

        var attributes = context.MethodInfo.GetCustomAttributes(true)
            .Concat(context.MethodInfo.DeclaringType?.GetCustomAttributes(true) ?? []);
        if (attributes.OfType<Microsoft.AspNetCore.Mvc.ConsumesAttribute>().Any())
            operation.Responses.TryAdd("415", new OpenApiResponse());
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
