namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>Ghi tài liệu tập giá trị đóng mà một tham số query của action này chấp nhận.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class QueryParameterEnumAttribute(string parameterName, params string[] values) : Attribute
{
    public string ParameterName { get; } = parameterName;

    public IReadOnlyList<string> Values { get; } = values;
}
