namespace Car_Rental_System_BE.Domain.Features.Identity;

public sealed record User(string Email, string FullName, string PasswordHash);
