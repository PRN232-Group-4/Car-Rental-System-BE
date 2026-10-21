namespace Car_Rental_System_BE.Application.DTOs.Operations;

public sealed record NavigationCounts(long Rentals, long Inspections, long Maintenance, long Reports, long Licenses)
{
    public static NavigationCounts Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record NavigationAccess(bool CustomerService, bool Maintenance, bool Inspections,
    bool AllReports = false, int? ReportStaffId = null);
