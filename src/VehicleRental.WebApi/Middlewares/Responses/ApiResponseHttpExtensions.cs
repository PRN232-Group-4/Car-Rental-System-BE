using Microsoft.AspNetCore.Diagnostics;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

public static class ApiResponseHttpExtensions
{
    /// <summary>Covers failures outside MVC, including authorization and routing.</summary>
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
                    UnauthorizedAccessException => StatusCodes.Status403Forbidden,
                    KeyNotFoundException => StatusCodes.Status404NotFound,
                    ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError
                };
                var message = statusCode == StatusCodes.Status500InternalServerError
                    ? "An unexpected server error occurred."
                    : exception?.Message;
                context.Response.StatusCode = statusCode;
                await context.Response.WriteAsJsonAsync(ApiResponse.Failure(statusCode, message: message), context.RequestAborted);
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
