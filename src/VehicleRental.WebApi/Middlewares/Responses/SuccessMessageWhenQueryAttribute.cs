using Microsoft.AspNetCore.Http;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Câu chữ mà phản hồi thành công của action này mang khi request chọn một biến thể cụ thể của operation
/// bằng tham số query, ví dụ giai đoạn sau thuê của endpoint kiểm tra xe.
/// </summary>
/// <remarks>
/// Được ưu tiên hơn <see cref="SuccessMessageAttribute"/>, attribute này vẫn ghi tài liệu cho biến thể mặc
/// định. So khớp chính xác: tham số phải mang đúng một giá trị này.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class SuccessMessageWhenQueryAttribute(string parameterName, string value, string message) : Attribute
{
    public string ParameterName { get; } = parameterName;

    public string Value { get; } = value;

    public string Message { get; } = message;

    public bool Matches(HttpRequest request) => request.Query[ParameterName] == Value;
}
