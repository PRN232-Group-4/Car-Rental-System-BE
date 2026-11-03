using Car_Rental_System_BE.Domain.Assignments;
using Xunit;

namespace VehicleRental.Tests;

public sealed class DomainAssignmentTests
{
    private static readonly DateTime RentalEndUtc = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    // 2026-10-15 12:00:00 UTC = 2026-10-15 19:00:00 GMT+7 (Date is 2026-10-15)
    private static readonly DateOnly ValidExpiry = new(2026, 10, 16);
    private static readonly DateOnly SameDayExpiry = new(2026, 10, 15);
    private static readonly DateOnly ExpiredDate = new(2026, 10, 14);

    [Fact]
    public void DriverEligibility_returns_true_when_all_conditions_satisfied()
    {
        var (eligible, reasons) = DriverEligibility.Evaluate(isDriver: true, isActive: true, ValidExpiry, RentalEndUtc);

        Assert.True(eligible);
        Assert.Empty(reasons);
    }

    [Fact]
    public void DriverEligibility_returns_all_reasons_when_all_conditions_fail()
    {
        var (eligible, reasons) = DriverEligibility.Evaluate(isDriver: false, isActive: false, ExpiredDate, RentalEndUtc);

        Assert.False(eligible);
        Assert.Equal(3, reasons.Count);
        Assert.Contains(DriverEligibility.NotADriver, reasons);
        Assert.Contains(DriverEligibility.Inactive, reasons);
        Assert.Contains(DriverEligibility.LicenseExpiring, reasons);
    }

    [Fact]
    public void DriverEligibility_accepts_license_expiring_on_same_local_calendar_day()
    {
        var (eligible, reasons) = DriverEligibility.Evaluate(isDriver: true, isActive: true, SameDayExpiry, RentalEndUtc);

        Assert.True(eligible);
        Assert.Empty(reasons);
    }

    [Fact]
    public void DriverEligibility_flags_license_expiring_before_rental_local_end()
    {
        // 2026-10-15 20:00:00 UTC = 2026-10-16 03:00:00 GMT+7 => local date is 2026-10-16!
        var lateEndUtc = new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc);
        var (eligible, reasons) = DriverEligibility.Evaluate(isDriver: true, isActive: true, SameDayExpiry, lateEndUtc);

        Assert.False(eligible);
        Assert.Contains(DriverEligibility.LicenseExpiring, reasons);
    }

    [Theory]
    [InlineData("pre_inspection", true)]
    [InlineData("renting", true)]
    [InlineData("pending", false)]
    [InlineData("completed", false)]
    [InlineData("cancelled", false)]
    [InlineData("post_inspection", false)]
    public void RentalAssignmentPolicy_only_allows_pre_inspection_or_renting(string status, bool expected)
    {
        Assert.Equal(expected, RentalAssignmentPolicy.CanAssignOrReplace(status));
    }

    [Theory]
    [InlineData("approved", 2026, 10, 16, true)]
    [InlineData("approved", 2026, 10, 15, true)]
    [InlineData("approved", 2026, 10, 14, false)]
    [InlineData("pending", 2026, 10, 16, false)]
    [InlineData("rejected", 2026, 10, 16, false)]
    [InlineData(null, 2026, 10, 16, false)]
    public void RentalAssignmentPolicy_checks_customer_license_correctly(string? status, int y, int m, int d, bool expected)
    {
        var expiry = new DateOnly(y, m, d);
        Assert.Equal(expected, RentalAssignmentPolicy.IsCustomerLicenseValid(status, expiry, RentalEndUtc));
    }

    [Theory]
    [InlineData("assigned", true)]
    [InlineData("reassigned", true)]
    [InlineData("completed", true)]
    [InlineData("cancelled", true)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void AssignmentStatus_validates_known_statuses(string? status, bool expected)
    {
        Assert.Equal(expected, AssignmentStatus.IsValid(status));
    }

    [Theory]
    [InlineData("breakdown", true)]
    [InlineData("accident", true)]
    [InlineData("maintenance", false)]
    [InlineData("customer_request", false)]
    [InlineData("initial", false)]
    public void AssignmentReason_identifies_incident_reasons(string reason, bool expected)
    {
        Assert.Equal(expected, AssignmentReason.IsIncident(reason));
    }
}
