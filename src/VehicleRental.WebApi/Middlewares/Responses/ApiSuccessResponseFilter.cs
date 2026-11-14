using Car_Rental_System_BE.Application.DTOs.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Normalizes controller successes and errors, including automatic validation results.
/// </summary>
public sealed class ApiSuccessResponseFilter : IAsyncAlwaysRunResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var statusCode = GetStatusCode(context.Result, context.HttpContext.Response.StatusCode);
        if (statusCode >= 400)
        {
            // Keep Challenge/Forbid results intact so their authentication handlers still run.
            // Empty authentication responses are normalized by status-code pages afterwards.
            switch (context.Result)
            {
                case ObjectResult result when result.Value is not ApiResponse:
                    result.Value = ApiResponse.Failure(statusCode, result.Value);
                    result.DeclaredType = null;
                    result.StatusCode = statusCode;
                    result.ContentTypes.Clear();
                    result.ContentTypes.Add("application/json");
                    break;
                case JsonResult result when result.Value is not ApiResponse:
                    result.Value = ApiResponse.Failure(statusCode, result.Value);
                    result.StatusCode = statusCode;
                    result.ContentType = "application/json";
                    break;
                case StatusCodeResult:
                    context.Result = new ObjectResult(ApiResponse.Failure(statusCode)) { StatusCode = statusCode };
                    break;
            }
            return next();
        }
        if (statusCode is < 200 or >= 300)
            return next();

        var message = GetSuccessMessage(context, statusCode);

        switch (context.Result)
        {
            case ObjectResult objectResult when objectResult.Value is not ApiResponse:
                objectResult.Value = ApiResponse.Create(objectResult.Value, message, statusCode);
                // The envelope now replaces the action's value, so the declared action type no
                // longer matches. Leaving it set makes typed actions (e.g. Task<VehiclePage>)
                // fail with InvalidCastException while serializing the response.
                objectResult.DeclaredType = null;
                break;
            case JsonResult jsonResult when jsonResult.Value is not ApiResponse:
                jsonResult.Value = ApiResponse.Create(jsonResult.Value, message, statusCode);
                break;
            case NoContentResult:
            case StatusCodeResult:
                context.Result = new OkObjectResult(ApiResponse.Create(null, message));
                break;
            case EmptyResult:
                context.Result = new OkObjectResult(ApiResponse.Create(null, message));
                break;
        }

        return next();
    }

    private static int GetStatusCode(IActionResult result, int responseStatusCode) => result switch
    {
        IStatusCodeActionResult { StatusCode: { } statusCode } => statusCode,
        ObjectResult { Value: ProblemDetails { Status: { } statusCode } } => statusCode,
        _ => responseStatusCode
    };

    private static string GetSuccessMessage(ResultExecutingContext context, int statusCode)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
            return "Request completed successfully.";

        // A synthesised descriptor without a method has no declared wording, so it keeps the generated one.
        if (action.MethodInfo is { } method &&
            Wording.GetOrAdd(method, DiscoverWording).For(context.HttpContext.Request) is { } declared)
            return declared;

        // Actions that declare no wording keep the message derived from their name, verb and status code.
        var operation = Regex.Replace(action.ActionName, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
        return statusCode switch
        {
            StatusCodes.Status201Created => $"{operation} created successfully.",
            StatusCodes.Status202Accepted => $"{operation} accepted successfully.",
            _ when HttpMethods.IsGet(context.HttpContext.Request.Method) => $"{operation} retrieved successfully.",
            _ when HttpMethods.IsDelete(context.HttpContext.Request.Method) => $"{operation} deleted successfully.",
            _ when HttpMethods.IsPut(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method) => $"{operation} updated successfully.",
            _ => $"{operation} completed successfully."
        };
    }

    /// <summary>
    /// Attributes are immutable per action method, and this runs on every successful response, so the
    /// declaration is read once per action.
    /// </summary>
    private static ActionWording DiscoverWording(MethodInfo method) => new(
        method.GetCustomAttributes<SuccessMessageAttribute>(inherit: true).Select(attribute => attribute.Message).FirstOrDefault(),
        method.GetCustomAttributes<SuccessMessageWhenQueryAttribute>(inherit: true).ToArray());

    private static readonly ConcurrentDictionary<MethodInfo, ActionWording> Wording = new();

    /// <summary>The wording an action declares for itself; null when it declares none.</summary>
    private sealed class ActionWording(string? message, SuccessMessageWhenQueryAttribute[] whenQuery)
    {
        public string? For(HttpRequest request) => whenQuery.FirstOrDefault(attribute => attribute.Matches(request))?.Message ?? message;
    }
}
