namespace Car_Rental_System_BE.Application.DTOs.Customers;

public sealed record CustomerDetail(int Id, int UserId, string Email, string? FullName, string? Gender,
    string? Phone, string? AvatarUrl, bool IsActive, CustomerLicenseDetail License, CustomerLoyaltyDetail Loyalty);

public sealed record CustomerLicenseDetail(string? Number, string? Type, DateOnly? IssueDate,
    DateOnly? ExpiryDate, string? ImageUrl, string Status);

public sealed record CustomerLoyaltyDetail(int Points);
