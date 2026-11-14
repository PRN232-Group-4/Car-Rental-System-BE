namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed record StartRegistration(string Email, string FullName, string Password);
public sealed record VerifyRegistration(string Email, string Otp);
