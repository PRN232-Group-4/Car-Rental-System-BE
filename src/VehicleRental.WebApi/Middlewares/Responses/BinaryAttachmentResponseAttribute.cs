namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// Marks an action whose successful response is a binary attachment instead of the JSON envelope: the
/// documented response keeps only these content types, each as a binary string, and no envelope schema.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class BinaryAttachmentResponseAttribute(string description, params string[] contentTypes) : Attribute
{
    public string Description { get; } = description;

    public IReadOnlyList<string> ContentTypes { get; } = contentTypes;
}
