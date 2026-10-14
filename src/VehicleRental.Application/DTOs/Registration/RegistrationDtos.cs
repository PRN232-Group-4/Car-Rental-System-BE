
namespace Car_Rental_System_BE.Application.DTOs.Registration;

public sealed record StartRegistration(string Email, string FullName, string Password);
public sealed record VerifyRegistration(string Email, string Otp);

public sealed record StoredImage(string SecureUrl, string PublicId);
