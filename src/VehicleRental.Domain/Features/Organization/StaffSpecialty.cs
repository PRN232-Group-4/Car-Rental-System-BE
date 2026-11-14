namespace Car_Rental_System_BE.Domain.Features.Organization;

public static class StaffSpecialty
{
    public static void Validate(string? role, bool isDriver)
    {
        if (role is not (null or "CustomerService" or "Driver" or "Maintenance"))
            throw new ArgumentException("StaffRole must be CustomerService, Driver or Maintenance.");
        if ((role == "Driver" && !isDriver) || (isDriver && role is not (null or "Driver")))
            throw new ArgumentException("StaffRole must match IsDriver.");
    }
}
