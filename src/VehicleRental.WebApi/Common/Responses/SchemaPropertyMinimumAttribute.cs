namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>
/// Ghi tài liệu cận dưới của một property trong schema request có tên mà action dùng, thay vì lặp lại
/// toàn bộ mô tả schema trên action.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class SchemaPropertyMinimumAttribute(string schemaId, string propertyName, int minimum) : Attribute
{
    public string SchemaId { get; } = schemaId;

    public string PropertyName { get; } = propertyName;

    public int Minimum { get; } = minimum;
}
