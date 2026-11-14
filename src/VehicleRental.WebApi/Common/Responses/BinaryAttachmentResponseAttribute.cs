namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>
/// Đánh dấu action có phản hồi thành công là tệp nhị phân đính kèm thay vì lớp bọc JSON: phản hồi được
/// ghi tài liệu chỉ giữ các content type này, mỗi cái là chuỗi nhị phân, và không có schema lớp bọc.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class BinaryAttachmentResponseAttribute(string description, params string[] contentTypes) : Attribute
{
    public string Description { get; } = description;

    public IReadOnlyList<string> ContentTypes { get; } = contentTypes;
}
