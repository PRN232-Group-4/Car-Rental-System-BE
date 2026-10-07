namespace Car_Rental_System_BE.Application.Accounts;

public static class PasswordRules
{
    public const int MinimumLength = 6;
    public const int MaximumLength = 20;
    public const string LengthErrorMessage = "Mật khẩu phải có từ {2} đến {1} ký tự.";
}
