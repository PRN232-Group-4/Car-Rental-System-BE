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
/// Chuẩn hóa kết quả thành công và lỗi của controller, kể cả kết quả kiểm tra hợp lệ tự động.
/// </summary>
public sealed class ApiSuccessResponseFilter : IAsyncAlwaysRunResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var statusCode = GetStatusCode(context.Result, context.HttpContext.Response.StatusCode);
        if (statusCode >= 400)
        {
            // Giữ nguyên kết quả Challenge/Forbid để các handler xác thực của chúng vẫn chạy. Phản
            // hồi xác thực rỗng sẽ được status-code pages chuẩn hóa sau.
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
                // Lớp bọc giờ thay thế giá trị của action, nên kiểu action đã khai báo không còn
                // khớp. Nếu để nguyên, các action có kiểu (ví dụ Task<VehiclePage>) sẽ lỗi
                // InvalidCastException khi serialize phản hồi.
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

        // Descriptor tổng hợp không có method thì không có câu chữ khai báo, nên giữ câu chữ được sinh ra.
        if (action.MethodInfo is { } method &&
            Wording.GetOrAdd(method, DiscoverWording).For(context.HttpContext.Request) is { } declared)
            return declared;

        // Action không khai báo câu chữ thì giữ thông điệp suy ra từ tên, verb và mã trạng thái.
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
    /// Attribute không đổi theo từng method action, và đoạn này chạy trên mọi phản hồi thành công,
    /// nên phần khai báo chỉ được đọc một lần cho mỗi action.
    /// </summary>
    private static ActionWording DiscoverWording(MethodInfo method) => new(
        method.GetCustomAttributes<SuccessMessageAttribute>(inherit: true).Select(attribute => attribute.Message).FirstOrDefault(),
        method.GetCustomAttributes<SuccessMessageWhenQueryAttribute>(inherit: true).ToArray());

    private static readonly ConcurrentDictionary<MethodInfo, ActionWording> Wording = new();

    /// <summary>Câu chữ mà action tự khai báo; null khi không khai báo.</summary>
    private sealed class ActionWording(string? message, SuccessMessageWhenQueryAttribute[] whenQuery)
    {
        public string? For(HttpRequest request) => whenQuery.FirstOrDefault(attribute => attribute.Matches(request))?.Message ?? message;
    }
}
