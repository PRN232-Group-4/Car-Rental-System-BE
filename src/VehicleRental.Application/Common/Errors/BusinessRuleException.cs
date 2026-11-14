namespace Car_Rental_System_BE.Application.Common.Errors;

/// <summary>Lỗi vi phạm quy tắc nghiệp vụ, kèm <see cref="Code"/> ổn định cho máy đọc và mã HTTP tương ứng.</summary>
public class BusinessRuleException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class ConflictException(string code, string message) : BusinessRuleException(code, message, 409);
public sealed class NotFoundException(string code, string message) : BusinessRuleException(code, message, 404);
