namespace Car_Rental_System_BE.Application.Features.Identity;

public sealed class AuthenticationLimitException() : Exception("Bạn đã thử quá nhiều lần. Vui lòng thử lại sau.");
