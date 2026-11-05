using Microsoft.AspNetCore.Http;

namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// The wording a successful response from this action carries when the request selects one specific variant
/// of the operation with a query parameter, e.g. the post-rental phase of an inspection endpoint.
/// </summary>
/// <remarks>
/// Takes precedence over <see cref="SuccessMessageAttribute"/>, which keeps documenting the default variant.
/// The match is exact: the parameter must carry this single value.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class SuccessMessageWhenQueryAttribute(string parameterName, string value, string message) : Attribute
{
    public string ParameterName { get; } = parameterName;

    public string Value { get; } = value;

    public string Message { get; } = message;

    public bool Matches(HttpRequest request) => request.Query[ParameterName] == Value;
}
