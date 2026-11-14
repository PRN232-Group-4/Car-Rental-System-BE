namespace Car_Rental_System_BE.WebApi.Common.Responses;

/// <summary>Tóm tắt một dòng mà operation Swagger của action này mang.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperationSummaryAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
