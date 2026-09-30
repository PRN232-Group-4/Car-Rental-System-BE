namespace Car_Rental_System_BE.Domain.Users;

public sealed record User(string Email, string FullName, string PasswordHash);
