namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>Documents the closed set of values a query parameter of this action accepts.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class QueryParameterEnumAttribute(string parameterName, params string[] values) : Attribute
{
    public string ParameterName { get; } = parameterName;

    public IReadOnlyList<string> Values { get; } = values;
}
