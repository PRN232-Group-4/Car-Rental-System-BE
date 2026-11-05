namespace Car_Rental_System_BE.WebApi.Middlewares.Responses;

/// <summary>
/// The wording a successful response from this action carries in the envelope's <c>message</c> field.
/// </summary>
/// <remarks>
/// The wording lives on the action it belongs to, so splitting or renaming a controller keeps the message
/// instead of silently falling back to the generic sentence generated from the action name, verb and status.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SuccessMessageAttribute(string message) : Attribute
{
    public string Message { get; } = message;
}
