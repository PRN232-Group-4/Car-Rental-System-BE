namespace Car_Rental_System_BE.Application.Common.Errors;

/// <summary>A business-rule failure carrying a stable machine-readable <see cref="Code"/> and the HTTP status it maps to.</summary>
public class BusinessRuleException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class ConflictException(string code, string message) : BusinessRuleException(code, message, 409);
public sealed class NotFoundException(string code, string message) : BusinessRuleException(code, message, 404);
public sealed class ForbiddenException(string code, string message) : BusinessRuleException(code, message, 403);
