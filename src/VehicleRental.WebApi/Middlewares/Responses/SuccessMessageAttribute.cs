namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Câu chữ mà phản hồi thành công của action này mang trong trường <c>message</c> của lớp bọc.
/// </summary>
/// <remarks>
/// Câu chữ nằm trên chính action nó thuộc về, nên tách hay đổi tên controller vẫn giữ thông điệp thay vì âm
/// thầm quay về câu chung sinh từ tên action, verb và trạng thái.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SuccessMessageAttribute(string message) : Attribute
{
    public string Message { get; } = message;
}
