namespace Car_Rental_System_BE.Domain.Assignments;

public static class AssignmentStatus
{
    public const string Assigned = "assigned";
    public const string Reassigned = "reassigned";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Assigned, Reassigned, Completed, Cancelled
    };

    public static bool IsValid(string? status) => status is not null && All.Contains(status);
}

public static class AssignmentReason
{
    public const string Initial = "initial";
    public const string DriverUnavailable = "driver_unavailable";
    public const string DriverRequest = "driver_request";
    public const string CustomerRequest = "customer_request";
    public const string Breakdown = "breakdown";
    public const string Accident = "accident";
    public const string Maintenance = "maintenance";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> DriverReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Initial, DriverUnavailable, DriverRequest, CustomerRequest, Other
    };

    public static readonly IReadOnlySet<string> VehicleReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Breakdown, Accident, Maintenance, CustomerRequest, Other
    };

    public static bool IsIncident(string? reason) =>
        string.Equals(reason, Breakdown, StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, Accident, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidDriverReason(string? reason) => reason is not null && DriverReasons.Contains(reason);
    public static bool IsValidVehicleReason(string? reason) => reason is not null && VehicleReasons.Contains(reason);
}

public static class DriverEligibility
{
    public const string NotADriver = "NOT_A_DRIVER";
    public const string Inactive = "INACTIVE";
    public const string LicenseExpiring = "LICENSE_EXPIRING";
    public const string Busy = "BUSY";

    public static (bool Eligible, IReadOnlyList<string> Reasons) Evaluate(
        bool isDriver, bool isActive, DateOnly? licenseExpiry, DateTime rentalEndUtc)
    {
        var reasons = new List<string>();
        if (!isDriver) reasons.Add(NotADriver);
        if (!isActive) reasons.Add(Inactive);

        // Convert rental end to GMT+7 Date to ensure the driver's license covers the entire local calendar day
        var localEnd = DateOnly.FromDateTime(rentalEndUtc.AddHours(7));
        if (licenseExpiry is null || licenseExpiry.Value < localEnd)
            reasons.Add(LicenseExpiring);

        return (reasons.Count == 0, reasons);
    }
}

public static class RentalAssignmentPolicy
{
    public static bool CanAssignOrReplace(string rentalStatus) =>
        rentalStatus is "pre_inspection" or "renting";

    public static bool IsCustomerLicenseValid(string? licenseStatus, DateOnly? expiryDate, DateTime rentalEndUtc)
    {
        if (!string.Equals(licenseStatus, "approved", StringComparison.OrdinalIgnoreCase))
            return false;

        var localEnd = DateOnly.FromDateTime(rentalEndUtc.AddHours(7));
        return expiryDate is not null && expiryDate.Value >= localEnd;
    }
}
