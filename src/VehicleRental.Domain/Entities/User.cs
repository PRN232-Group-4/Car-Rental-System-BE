namespace Car_Rental_System_BE.Domain.Entities;

public sealed record User(string Email, string FullName, string PasswordHash);
