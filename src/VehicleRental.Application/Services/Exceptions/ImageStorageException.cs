namespace Car_Rental_System_BE.Application.Services.Exceptions;

/// <summary>An infrastructure upload failure, never a customer input validation error.</summary>
public sealed class ImageStorageException(string message) : Exception(message);
