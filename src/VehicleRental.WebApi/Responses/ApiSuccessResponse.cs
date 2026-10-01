using System.Text.Json.Serialization;

namespace Car_Rental_System_BE.Responses;

/// <summary>
/// Standard body returned by successful API operations.
/// </summary>
public sealed record ApiSuccessResponse(
    string Message,
    [property: JsonPropertyName("is_success")] bool IsSuccess,
    object? Data)
{
    public static ApiSuccessResponse Create(object? data, string message) => new(message, true, data);
}
