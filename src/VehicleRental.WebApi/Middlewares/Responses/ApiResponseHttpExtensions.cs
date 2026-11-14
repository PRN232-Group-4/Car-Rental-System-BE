using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Car_Rental_System_BE.Application.Common.Errors;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

public static class ApiResponseHttpExtensions
{
    /// <summary>Xử lý các lỗi xảy ra ngoài MVC, kể cả phân quyền và định tuyến.</summary>
    public static IApplicationBuilder UseApiResponseErrors(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            ExceptionHandler = async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiResponse");
                logger.LogError(exception, "Unhandled request exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
                var statusCode = exception switch
                {
                    BusinessRuleException business => business.StatusCode,
                    UnauthorizedAccessException => StatusCodes.Status403Forbidden,
                    KeyNotFoundException => StatusCodes.Status404NotFound,
                    ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                };
                var message = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected server error occurred."
                    : exception?.Message;
                context.Response.StatusCode = statusCode;
                var problem = new ProblemDetails { Status = statusCode, Detail = message };
                if (exception is BusinessRuleException rule) problem.Extensions["code"] = rule.Code;
                await context.Response.WriteAsJsonAsync(ApiResponse.Failure(statusCode, problem, message), context.RequestAborted);
            }
        });

        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            await context.Response.WriteAsJsonAsync(ApiResponse.Failure(context.Response.StatusCode), context.RequestAborted);
        });
        return app;
    }
}
