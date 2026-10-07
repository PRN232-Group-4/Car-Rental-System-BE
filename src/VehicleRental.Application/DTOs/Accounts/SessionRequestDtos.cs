using Car_Rental_System_BE.Application.Validators;
using System.ComponentModel.DataAnnotations;

namespace Car_Rental_System_BE.Application.DTOs.Accounts;

public sealed record LoginRequest([param: Required, EmailAddress] string Email, [param: Required, StringLength(PasswordRules.MaximumLength, MinimumLength = PasswordRules.MinimumLength, ErrorMessage = PasswordRules.LengthErrorMessage)] string Password, bool RememberMe = false);
