namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>
/// Một phần mô tả operation Swagger của action này. Nhiều attribute trên cùng action được nối theo thứ tự
/// khai báo, đúng như đã viết (kể cả khoảng trắng đầu), để mô tả nhiều đoạn được viết ngay cạnh action
/// thay vì ghép từ tên controller và action.
/// </summary>
/// <remarks>
/// Văn bản là toàn bộ mô tả của action: filter operation chỉ nối thêm câu quy ước đặt tên route dùng
/// chung sau nó.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class OperationDescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;

    /// <summary>
    /// Đồng thời ghi tài liệu dạng phân trang của operation này: mọi tham số <c>page</c>/<c>pageSize</c>
    /// được gán giá trị nhỏ nhất, mặc định và lớn nhất đã ghi tài liệu.
    /// </summary>
    public bool DocumentsPaginationParameters { get; init; }
}
