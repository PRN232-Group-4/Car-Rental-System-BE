namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>The one-line summary the Swagger operation of this action carries.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class OperationSummaryAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
