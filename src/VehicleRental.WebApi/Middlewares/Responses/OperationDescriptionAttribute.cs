namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// One part of the Swagger operation description of this action. Several attributes on the same action are
/// concatenated in declaration order, exactly as written (leading spaces included), so a multi-paragraph
/// description is authored next to the action instead of being assembled from controller and action names.
/// </summary>
/// <remarks>
/// The text is the whole description of the action: the operation filter only appends the cross-cutting
/// route-naming sentence after it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class OperationDescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;

    /// <summary>
    /// Also documents the pagination shape of this operation: every <c>page</c>/<c>pageSize</c> parameter is
    /// given the documented minimum, default and maximum.
    /// </summary>
    public bool DocumentsPaginationParameters { get; init; }
}
