namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Documents the lower bound of one property in a named request schema the action consumes, instead of
/// repeating the whole schema description on the action.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class SchemaPropertyMinimumAttribute(string schemaId, string propertyName, int minimum) : Attribute
{
    public string SchemaId { get; } = schemaId;

    public string PropertyName { get; } = propertyName;

    public int Minimum { get; } = minimum;
}
