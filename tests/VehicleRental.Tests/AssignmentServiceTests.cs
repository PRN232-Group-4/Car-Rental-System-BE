using Car_Rental_System_BE.Application.Assignments;
using Car_Rental_System_BE.Application.Availability;
using Car_Rental_System_BE.Application.Common.Errors;
using Car_Rental_System_BE.Domain.Assignments;
using Car_Rental_System_BE.Domain.Availability;
using Microsoft.Extensions.Options;
using Xunit;

namespace VehicleRental.Tests;

public sealed class AssignmentServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
    private readonly TestTimeProvider _timeProvider = new(FixedNow);
    private readonly StubAssignmentRepo _repo = new();
    private readonly StubAvailabilityService _availability = new();

    private AssignmentService CreateService(AssignmentOptions? options = null) =>
        new(_repo, _availability, options ?? new AssignmentOptions(), _timeProvider);

    [Fact]
    public async Task AssignDriver_throws_NotFound_when_rental_does_not_exist()
    {
        var service = CreateService();
        _repo.Schedule = null;

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AssignDriverAsync(new(999, 1, "initial", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("RENTAL_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_Conflict_when_rental_state_is_illegal()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "pending", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, 1, "initial", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("ILLEGAL_RENTAL_STATE", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_PreInspectionRequired_on_initial_assignment_without_inspection()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "pre_inspection", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.HasExistingAssignment = false;
        _repo.PassedPreInspection = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, 1, "initial", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("PRE_INSPECTION_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_DriverRequired_when_has_driver_but_staff_id_null()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "pre_inspection", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.HasExistingAssignment = true;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, null, "driver_unavailable", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("DRIVER_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_DriverNotEligible_when_driver_has_conflict()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "renting", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.HasExistingAssignment = true;
        _repo.Candidates = [new(5, "Busy Driver", null, 4.5m, true, true, new DateOnly(2026, 11, 1), HasScheduleConflict: true)];

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, 5, "driver_unavailable", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("DRIVER_NOT_ELIGIBLE", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_DriverNotAllowed_when_self_drive_supplies_driver()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "pre_inspection", false, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.HasExistingAssignment = false;
        _repo.PassedPreInspection = true;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, 5, "initial", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("DRIVER_NOT_ALLOWED_FOR_SELF_DRIVE", ex.Code);
    }

    [Fact]
    public async Task AssignDriver_throws_CustomerLicenseNotValid_when_license_is_pending()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "pre_inspection", false, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "pending", new DateOnly(2026, 11, 1));
        _repo.HasExistingAssignment = false;
        _repo.PassedPreInspection = true;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDriverAsync(new(1, null, "initial", null, "test@test.local"), CancellationToken.None));

        Assert.Equal("CUSTOMER_LICENSE_NOT_VALID", ex.Code);
    }

    [Fact]
    public async Task ReplaceVehicle_throws_SameVehicle_when_new_id_equals_current()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "renting", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ReplaceVehicleAsync(new(1, 10, "breakdown", "Flat tire", true, "test@test.local"), CancellationToken.None));

        Assert.Equal("SAME_VEHICLE_REPLACEMENT", ex.Code);
    }

    [Fact]
    public async Task ReplaceVehicle_throws_TypeMismatch_when_types_differ()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "renting", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.VehicleValidAndSameType = false;

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReplaceVehicleAsync(new(1, 20, "breakdown", "Flat tire", true, "test@test.local"), CancellationToken.None));

        Assert.Equal("VEHICLE_TYPE_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task ReplaceVehicle_throws_Unavailable_when_new_vehicle_is_busy()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "renting", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.VehicleValidAndSameType = true;
        _availability.Result = AvailabilityResult.Blocked(AvailabilityReason.Booked);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReplaceVehicleAsync(new(1, 20, "breakdown", "Flat tire", true, "test@test.local"), CancellationToken.None));

        Assert.Equal("VEHICLE_UNAVAILABLE", ex.Code);
    }

    [Fact]
    public async Task ReplaceVehicle_succeeds_when_all_conditions_met()
    {
        var service = CreateService();
        _repo.Schedule = new(1, 10, 1, 1, "renting", true, 100, FixedNow.AddHours(1), FixedNow.AddHours(5), "approved", new DateOnly(2026, 11, 1));
        _repo.VehicleValidAndSameType = true;
        _availability.Result = AvailabilityResult.Free;

        var result = await service.ReplaceVehicleAsync(new(1, 20, "breakdown", "Flat tire", true, "test@test.local"), CancellationToken.None);

        Assert.True(result.RequiresInspection);
        Assert.Equal(20, result.Assignment.VehicleId);
    }

    private sealed class StubAssignmentRepo : IAssignmentRepository
    {
        public RentalScheduleInfo? Schedule { get; set; }
        public bool PassedPreInspection { get; set; } = true;
        public bool HasExistingAssignment { get; set; } = false;
        public bool VehicleValidAndSameType { get; set; } = true;
        public IReadOnlyList<DriverCandidateRaw> Candidates { get; set; } = [];

        public Task<RentalScheduleInfo?> GetRentalScheduleAsync(int rentalId, CancellationToken ct) => Task.FromResult(Schedule);
        public Task<bool> HasPassedPreInspectionAsync(int rentalId, CancellationToken ct) => Task.FromResult(PassedPreInspection);
        public Task<bool> HasAnyAssignmentAsync(int rentalId, CancellationToken ct) => Task.FromResult(HasExistingAssignment);
        public Task<IReadOnlyList<AssignmentResponse>> ListAssignmentsAsync(int rentalId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AssignmentResponse>>([]);

        public Task<AssignmentResponse> AssignDriverAsync(
            RentalScheduleInfo rental, int? driverStaffId, string reason, string? note, string assignedByEmail,
            DateTime startsAt, DateTime endsAt, int bufferMinutes, CancellationToken ct) =>
            Task.FromResult(new AssignmentResponse(1, rental.RentalId, rental.VehicleId, driverStaffId, "Test Driver", "assigned", reason, note, null, startsAt, endsAt, null, FixedNow, assignedByEmail));

        public Task<VehicleReplacementResult> ReplaceVehicleAsync(
            RentalScheduleInfo rental, int newVehicleId, string reason, string? note, bool keepDriver, string assignedByEmail,
            DateTime startsAt, DateTime endsAt, int bufferMinutes, CancellationToken ct) =>
            Task.FromResult(new VehicleReplacementResult(
                new AssignmentResponse(2, rental.RentalId, newVehicleId, 5, "Test Driver", "assigned", reason, note, 1, startsAt, endsAt, null, FixedNow, assignedByEmail),
                RequiresInspection: true,
                IncidentMaintenanceId: 101));

        public Task<IReadOnlyList<DriverCandidateRaw>> ListDriverCandidatesAsync(DateTime windowStart, DateTime windowEnd, int bufferMinutes, int? excludeRentalId, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public Task<IReadOnlyList<AssignableVehicleItem>> ListAssignableVehiclesAsync(int vehicleTypeId, int? currentFacilityId, DateTime windowStart, DateTime windowEnd, int bufferMinutes, int excludeRentalId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AssignableVehicleItem>>([]);

        public Task<bool> VehicleExistsAndActiveAsync(int vehicleId, int requiredTypeId, CancellationToken ct) =>
            Task.FromResult(VehicleValidAndSameType);
    }

    private sealed class StubAvailabilityService : IVehicleAvailabilityService
    {
        public AvailabilityResult Result { get; set; } = AvailabilityResult.Free;

        public Task<AvailabilityResult> CheckAsync(int vehicleId, TimeRange window, CancellationToken ct) => Task.FromResult(Result);
        public Task AssertAvailableAsync(System.Data.Common.DbTransaction transaction, int vehicleId, TimeRange window, int? excludeRentalId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
