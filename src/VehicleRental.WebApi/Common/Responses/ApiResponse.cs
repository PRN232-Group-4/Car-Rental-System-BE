using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>Hợp đồng JSON dùng chung cho phản hồi API thành công và thất bại.</summary>
public sealed record ApiResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_success")] bool IsSuccess,
    [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? Data,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? Error)
{
    public static ApiResponse Create(object? data, string message, int code = StatusCodes.Status200OK) =>
        new(code, "success", true, data, message, null);

    public static ApiResponse Failure(int code, object? error = null, string? message = null)
    {
        var problem = error as ProblemDetails;
        message ??= problem?.Detail ?? problem?.Title ?? ReasonPhrases.GetReasonPhrase(code);
        error ??= new ProblemDetails { Status = code, Title = ReasonPhrases.GetReasonPhrase(code), Detail = message };
        return new(code, "error", false, null, message, error);
    }
}
