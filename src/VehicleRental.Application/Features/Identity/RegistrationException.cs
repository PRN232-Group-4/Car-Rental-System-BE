namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class RegistrationException(string message, int statusCode) : Exception(message) { public int StatusCode { get; } = statusCode; }
