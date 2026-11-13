using Car_Rental_System_BE.Domain.Features.Rentals;
using Xunit;

namespace VehicleRental.Tests;

/// <summary>
/// Phase 2 / D2 money rule: the overtime of a subscription rental is split between the package hours that are
/// still left and the hours the customer has to pay for. Every fact here is a money fact, so the split is
/// asserted exactly - including that the package is never charged for more hours than the rental actually ran over.
/// </summary>
public sealed class PackageHourAllocationTests
{
    private const decimal UnitHourly = 100_000m;

    [Fact]
    public void Package_covers_the_whole_overtime_and_nothing_is_billed()
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 4, remainingPackageHours: 10m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(4m, allocation.HoursCharged);
        Assert.Equal(0m, allocation.ExcessHours);
        Assert.Equal(0m, allocation.ExcessAmount);
        Assert.Equal(0m, allocation.ExcessVat);
    }

    [Fact]
    public void Package_that_covers_only_part_of_the_overtime_bills_the_rest()
    {
        // The Phase 0 / scenario 0.4 numbers: 5 hours late, 2 package hours left, 3 x 100,000 + 10% VAT.
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 5, remainingPackageHours: 2m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(2m, allocation.HoursCharged);
        Assert.Equal(3m, allocation.ExcessHours);
        Assert.Equal(300_000m, allocation.ExcessAmount);
        Assert.Equal(30_000m, allocation.ExcessVat);
    }

    [Fact]
    public void Exhausted_package_bills_every_overtime_hour_and_takes_nothing_from_the_package()
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 5, remainingPackageHours: 0m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(0m, allocation.HoursCharged);
        Assert.Equal(5m, allocation.ExcessHours);
        Assert.Equal(500_000m, allocation.ExcessAmount);
        Assert.Equal(50_000m, allocation.ExcessVat);
    }

    [Fact]
    public void Fractional_package_hours_are_split_exactly()
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 5, remainingPackageHours: 2.5m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(2.5m, allocation.HoursCharged);
        Assert.Equal(2.5m, allocation.ExcessHours);
        Assert.Equal(250_000m, allocation.ExcessAmount);
        Assert.Equal(25_000m, allocation.ExcessVat);
    }

    [Fact]
    public void Unlimited_package_covers_everything()
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 7, remainingPackageHours: null,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(7m, allocation.HoursCharged);
        Assert.Equal(0m, allocation.ExcessHours);
        Assert.Equal(0m, allocation.ExcessAmount);
    }

    [Fact]
    public void The_overtime_fee_rate_applies_to_the_billed_excess()
    {
        // 2 excess hours at 100,000 with a 25% late fee: 250,000 + 10% VAT.
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 4, remainingPackageHours: 2m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 25m, vatRate: 10m);

        Assert.Equal(2m, allocation.HoursCharged);
        Assert.Equal(2m, allocation.ExcessHours);
        Assert.Equal(250_000m, allocation.ExcessAmount);
        Assert.Equal(25_000m, allocation.ExcessVat);
    }

    [Fact]
    public void No_overtime_means_no_charge()
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours: 0, remainingPackageHours: 3m,
            unitHourlyPrice: UnitHourly, overtimeFeeRate: 0m, vatRate: 10m);

        Assert.Equal(0m, allocation.HoursCharged);
        Assert.Equal(0m, allocation.ExcessHours);
        Assert.Equal(0m, allocation.ExcessAmount);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(1, 0.5)] [InlineData(3, 3)] [InlineData(10, 1)] [InlineData(10, 100)]
    public void Charged_plus_excess_always_equals_the_overtime(int overtimeHours, decimal remaining)
    {
        var allocation = PackageHourAllocator.Allocate(overtimeHours, remaining, UnitHourly, 0m, 10m);

        Assert.Equal(overtimeHours, allocation.HoursCharged + allocation.ExcessHours);
        Assert.True(allocation.HoursCharged <= overtimeHours);
        Assert.True(allocation.HoursCharged <= remaining);
    }
}
