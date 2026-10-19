using System.Data.Common;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Availability;
using Xunit;

namespace VehicleRental.Tests;

public sealed class VehicleSearchServiceTests
{
    private static readonly DateTime Now = new(2030, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private readonly TestTimeProvider _time = new(Now);
    private readonly FakeVehicleSearchRepository _repo = new();
    private readonly FakeVehicleAvailabilityService _availability = new();
    private readonly FakeDriverAvailability _drivers = new();
    private readonly AvailabilityOptions _options = new();

    private VehicleSearchService CreateService()
        => new(_repo, _availability, _drivers, _options, _time);

    [Fact]
    public async Task Search_with_sort_nearest_throws_SORT_NOT_SUPPORTED()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(null, null, Sort: "nearest"), CancellationToken.None));
        Assert.Equal("SORT_NOT_SUPPORTED", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Search_with_invalid_sort_throws_INVALID_SORT()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(null, null, Sort: "invalid"), CancellationToken.None));
        Assert.Equal("INVALID_SORT", ex.Code);
    }

    [Fact]
    public async Task Search_with_invalid_rental_type_throws_INVALID_RENTAL_TYPE()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(null, null, RentalType: "daily"), CancellationToken.None));
        Assert.Equal("INVALID_RENTAL_TYPE", ex.Code);
    }

    [Theory]
    [InlineData("2030-05-01T12:00:00Z", null)]
    [InlineData(null, "2030-05-01T14:00:00Z")]
    public async Task Search_with_half_window_throws_INVALID_TIME_RANGE(string? fromStr, string? toStr)
    {
        var service = CreateService();
        DateTime? from = fromStr is null ? null : DateTime.Parse(fromStr);
        DateTime? to = toStr is null ? null : DateTime.Parse(toStr);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(from, to), CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task Search_with_from_after_to_throws_INVALID_TIME_RANGE()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(Now.AddHours(2), Now.AddHours(1)), CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task Search_with_duration_exceeding_max_days_throws_INVALID_TIME_RANGE()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(Now.AddHours(1), Now.AddDays(367)), CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task Search_with_from_in_past_beyond_tolerance_throws_INVALID_TIME_RANGE()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(Now.AddMinutes(-6), Now.AddHours(2)), CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task Search_with_hourly_less_than_min_hourly_minutes_throws_INVALID_TIME_RANGE()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SearchAsync(new VehicleSearchQuery(Now.AddHours(1), Now.AddHours(1).AddMinutes(30), RentalType: "hourly"), CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task Search_with_has_driver_when_no_driver_available_returns_empty_result()
    {
        _drivers.IsAvailable = false;
        var service = CreateService();
        var result = await service.SearchAsync(new VehicleSearchQuery(Now.AddHours(1), Now.AddHours(3), HasDriver: true), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.NotNull(result.AppliedWindow);
    }

    [Fact]
    public async Task Search_checks_driver_in_selected_facility_only()
    {
        await CreateService().SearchAsync(new(Now.AddHours(1), Now.AddHours(3), FacilityId: 42, HasDriver: true), default);
        Assert.Equal(42, _drivers.FacilityId);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Search_rejects_invalid_pagination(int page, int size)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService().SearchAsync(new(null, null, Page: page, PageSize: size), default));
        Assert.Equal("INVALID_PAGINATION", error.Code);
    }

    [Fact]
    public async Task CheckAvailability_requires_both_from_and_to()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CheckAvailabilityAsync(1, Now.AddHours(1), null, CancellationToken.None));
        Assert.Equal("INVALID_TIME_RANGE", ex.Code);
    }

    [Fact]
    public async Task BusySlots_validates_range_and_checks_vehicle_exists()
    {
        _repo.Exists = false;
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.BusySlotsAsync(999, Now.AddHours(1), Now.AddHours(2), CancellationToken.None));
        Assert.Equal("VEHICLE_NOT_FOUND", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ActivePolicy_when_missing_throws_POLICY_NOT_FOUND()
    {
        _repo.ActivePolicy = null;
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ActivePolicyAsync(1, CancellationToken.None));
        Assert.Equal("POLICY_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task AffectedRentals_when_vehicle_not_found_throws()
    {
        _repo.Exists = false;
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AffectedRentalsAsync(999, CancellationToken.None));
        Assert.Equal("VEHICLE_NOT_FOUND", ex.Code);
    }

    private sealed class FakeVehicleSearchRepository : IVehicleSearchRepository
    {
        public bool Exists = true;
        public ActiveRentalPolicy? ActivePolicy = new(1, 1, "Standard", 10m, 20m, 5m);

        public Task<VehicleSearchResult> SearchAsync(VehicleSearchQuery query, int bufferMinutes, bool? driverAvailable, CancellationToken ct)
            => Task.FromResult(new VehicleSearchResult([], query.Page, query.PageSize, 0, null));

        public Task<ActiveRentalPolicy?> GetActivePolicyAsync(int vehicleTypeId, CancellationToken ct)
            => Task.FromResult(ActivePolicy);

        public Task<bool> VehicleExistsAsync(int vehicleId, CancellationToken ct)
            => Task.FromResult(Exists);

        public Task<IReadOnlyList<BusySlot>> ListBusySlotsAsync(int vehicleId, TimeRange window, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BusySlot>>([]);

        public Task<IReadOnlyList<AffectedRental>> ListAffectedRentalsAsync(int vehicleId, DateTime nowUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AffectedRental>>([]);
    }

    private sealed class FakeVehicleAvailabilityService : IVehicleAvailabilityService
    {
        public Task<AvailabilityResult> CheckAsync(int vehicleId, TimeRange window, CancellationToken ct)
            => Task.FromResult(AvailabilityResult.Free);

        public Task AssertAvailableAsync(DbTransaction transaction, int vehicleId, TimeRange window, int? excludeRentalId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeDriverAvailability : IDriverAvailability
    {
        public bool IsAvailable = true;
        public int? FacilityId;

        public Task<bool> AnyAvailableAsync(TimeRange? window, DateTime nowUtc, CancellationToken ct, int? facilityId = null)
        {
            FacilityId = facilityId;
            return Task.FromResult(IsAvailable);
        }
    }

    private sealed class TestTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
