
namespace Car_Rental_System_BE.Application.Validators;

public sealed class StaffLicenseException(string message) : ArgumentException(message);
